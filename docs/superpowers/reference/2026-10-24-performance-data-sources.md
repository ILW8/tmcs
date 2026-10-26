# CPU, Memory, Disk and Network data sources: how to reproduce Task Manager's Performance tab

Verified on 2026-10-24 on the owner's PC (Ryzen 9 7950X, 64 GB, Windows 10 Pro 19045, Taskmgr.exe 10.0.19041.4957). GPU is in `2026-10-24-gpu-data-sources.md`.

- **Ground truth:** `artifacts/reference/taskmgr/01`–`19` (captured 19:21–19:24 UTC) and `cpu-counters.csv`.
- **Probes:** a .NET 10 console app run 19:35–20:05 UTC, **not elevated** (medium integrity, filtered admin token, no `SeProfileSingleProcessPrivilege`). Elevated runs were not allowed, so "elevated" behaviour below is inferred.
- **Matching:** static values were compared exactly. Dynamic values were taken 15–40 minutes after the capture, so they are only plausibility checks. Where a capture holds several numbers from one tick (the memory tooltips), the arithmetic was checked exactly.
- **Labels:** "**unverified**" marks anything inferred and not proven against Task Manager.

## Verdict

- **Elevation:** no value on these four pages needs it.
  - The only calls refused were `CreateFile(\\.\PhysicalDriveN, GENERIC_READ)` and `IOCTL_DISK_GET_LENGTH_INFO`, which needs read access. Neither is needed: open with access 0 and use `IOCTL_DISK_GET_DRIVE_GEOMETRY_EX`.
  - `SystemMemoryListInformation` (80) and `SystemStoreInformation` (109) both succeeded without `SeProfileSingleProcessPrivilege`.
  - Debug (user) and Release (elevated) builds should show identical numbers.
- **CPU:** Task Manager's per-CPU and total utilization is PDH `Processor Information` `% Processor Utility`. `NtQuerySystemInformation(SystemProcessorPerformanceInformation)` is cheaper, but it reproduces `% Processor Time` instead (tick-quantized and 2–12 points lower here), so it can't be used.
- **Disk listing:** Task Manager lists disks that have a *present* `GUID_DEVINTERFACE_DISK` interface, numbered by `IOCTL_STORAGE_GET_DEVICE_NUMBER`. This gives exactly Disk 0–4.
  - The machine has no Storage Spaces. Disks 0 and 1 are LDM dynamic disks, and E: is one dynamic volume with an extent on each, so both show "(E:)".
- **Adapter listing:** `GetAdaptersAddresses(AF_UNSPEC, 0)`, kept in its own order, filtered to `OperStatus == Up` and `IfType != 24` (loopback). This gives exactly the 12 adapters in Task Manager's order.
- **Costs (per call, this machine):**

| Call | Cost | Use |
|---|---|---|
| PDH collect + `% Processor Utility(*)` array (34 instances) | 58 µs | per tick |
| … + `% Privileged Utility(*)` (kernel) | 89 µs total | per tick |
| … + a third wildcard counter | 167 µs total | prefer a single `_Total` instance for Speed |
| `NtQSI(SystemProcessorPerformanceInformation)` | 14 µs | not usable (see CPU) |
| `NtQSI(SystemProcessInformation)`, reused 4 MB buffer | 8.2 ms | tmcs already takes this snapshot each tick |
| `GetPerformanceInfo` | **12.6 ms** | **avoid**: it takes its own process snapshot |
| `NtQSI(SystemPerformanceInformation)` (2) | 0.9 µs | commit, pools, available |
| `NtQSI(SystemMemoryListInformation)` (80) | 0.2 µs | composition bar |
| `GlobalMemoryStatusEx` | 0.5 µs | |
| `NtQSI(SystemStoreInformation)`, compression stats | 10 µs | |
| `IOCTL_DISK_PERFORMANCE` | ~1 µs per disk | per tick |
| PDH `PhysicalDisk(*)`, 4 counters | 60 µs | alternative |
| `GetIfEntry2` | 24 µs per adapter | per tick (~0.3 ms for 12) |
| `GetIfTable2` (135 rows) | 4.7 ms | don't |
| `GetAdaptersAddresses(AF_UNSPEC, 0)` | 4.4 ms | on change only |
| `WlanQueryInterface(current_connection)` | 62 µs | |

## CPU

| Task Manager value | Source | Value here | Match? | Elevation | Notes |
|---|---|---|---|---|---|
| Utilization "10%" (pane), "14%" (sidebar) | PDH `\Processor Information(_Total)\% Processor Utility` | 7–19 % | yes (mean \|Δ\| 0.53 over 60 updates, `cpu-counters.csv`) | no | `% Processor Time` was off by 2.34. Integer percent. Rounding vs truncation **unverified**. |
| One graph per logical processor | PDH `\Processor Information(*)\% Processor Utility`, instances `"0,0"`–`"0,31"` | e.g. LP0 33.4, LP31 0.5 | **unverified** per LP (the charts carry no numbers); consistent with `_Total` | no | Values can exceed 100. Read with `PDH_FMT_NOCAP100`, then clamp. See "CPU rules". |
| Kernel time line ("Show kernel times") | PDH `\Processor Information(*)\% Privileged Utility` | LP0 16.7 of 33.4 | **unverified** | no | Same basis as Utility. `NtQSI` class 8 (Kernel − Idle) tracks `% Privileged Time` (3.1 vs 16.2 on one LP), not this. |
| Speed "4.42 GHz" | `_Total % Processor Performance` × base MHz / 100 | 98.6 % × 4500 = 4.44 GHz | yes (50/60 rows exact with × 4.50 GHz; 49/60 with × `Processor Frequency` 4501) | no | Both fit within one sample of jitter. "%.2f GHz". |
| Processes "481" (00-start: 485) | number of `SystemProcessInformation` entries, **including** System Idle Process (PID 0) | 479–483 | plausible | no | Equals `GetPerformanceInfo.ProcessCount` and PDH `System\Processes`. All three count Idle. Whether Task Manager does (±1) is **unverified**. |
| Threads "11654" | Σ `NumberOfThreads` (Idle contributes 32) | 11664–11686 | plausible | no | Equals `GetPerformanceInfo.ThreadCount`. ±32 **unverified**. |
| Handles "329969" | Σ `HandleCount` | 328177–329142 | plausible | no | Equals `GetPerformanceInfo.HandleCount` (±3 timing). |
| Up time "25:17:28:41" | now − `NtQSI(SystemTimeOfDayInformation).BootTime`, floored to seconds, `d:hh:mm:ss` | boot 2026-09-29 01:52:56.500 UTC | plausible: both boot sources fit | no | BootTime fits all 60 csv rows if the capture read the text within 50 ms of the refresh. `GetTickCount64` puts boot 114 ms earlier and fits too if the read lag was 60–110 ms. `capture-taskmgr.ps1` allows that lag: it polls every 50 ms and reads four counters before stamping the time. The capture can't tell the two apart: **unverified**. No sleep since boot, so sleep handling is **unverified** too (`SleepTimeBias` = 0). Taskmgr imports both `GetTickCount64` and `RtlTimeToElapsedTimeFields`. Days are not padded. |
| Base speed "4.50 GHz" | registry `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0\~MHz` = 4500, or `CallNtPowerInformation(ProcessorInformation).MaxMhz` = 4501 | both give "4.50" | yes | no | Taskmgr contains that registry path and imports `CallNtPowerInformation`. Which one it uses is **unverified**. `CurrentMhz` is stuck at 4501, so it's useless for Speed. |
| Sockets "1" | `GetLogicalProcessorInformationEx(RelationAll)`: count of `RelationProcessorPackage` | 1 | yes | no | |
| Cores "16" | count of `RelationProcessorCore` | 16 | yes | no | |
| Logical processors "32" | Σ popcount of the core `GroupMask`s | 32 | yes | no | |
| Virtualization "Enabled" | `IsProcessorFeaturePresent(PF_VIRT_FIRMWARE_ENABLED = 21)` | TRUE | yes | no | `PF_SECOND_LEVEL_ADDRESS_TRANSLATION` (20) is FALSE inside the Hyper-V root partition. MUI also has "Disabled" and "Not capable". |
| L1 cache "1.0 MB" | Σ `CacheSize` of level-1 **Data + Instruction** caches | 16 × 32 KB + 16 × 32 KB = 1.0 MB | yes | no | L1 is data and instruction summed. |
| L2 cache "16.0 MB" | Σ level-2 caches | 16 × 1 MB | yes | no | |
| L3 cache "64.0 MB" | Σ level-3 caches | 2 × 32 MB | yes | no | |

### CPU rules

- **PDH instance names and order:**
  - `PdhGetFormattedCounterArrayW` returns instances sorted as strings: `0,0 0,1 0,10 … 0,19 0,2 0,20 … 0,9 0,_Total _Total`.
  - Parse `group,number` from each name. Never use the array position.
  - Use `PdhAddEnglishCounterW` so the path doesn't depend on the display language.
  - The instance set is fixed, so the array buffer can be allocated once (spec 5.1).
- **Grid:** 8 columns × 4 rows, row-major in LP order. The chart rectangles confirm it (MSAA names `CPU00`, `CPU10` … `CPU310`, i.e. "CPU" + LP + "0").
- **`NtQSI(SystemProcessorPerformanceInformation)` (class 8):**
  - Busy % moves in 1/64-second steps (1.56 %) and equals PDH `% Processor Time`.
  - Under light load it reads 2–12 points below `% Processor Utility` (e.g. LP3 6.25 vs 18.35).
  - Busy × `% Processor Performance` doesn't close the gap. Not usable for a 1:1 copy.
- **Idle cycles (class 83, `SystemProcessorIdleCycleTimeInformation`), 30 µs:**
  - (1 − Δidle cycles ÷ (Δt × TSC 4.489 GHz)) × perf % lands 1–2 points below Utility on every LP (mean 7.1 vs 8.2).
  - Close, but not exact.
- **What Taskmgr imports:**
  - PDH (`PdhOpenQueryW`, `PdhAddCounterW`, `PdhGetFormattedCounterArrayW`, `PdhGetRawCounterArrayW`).
  - The PerfLib V2 consumer API (`PerfOpenQueryHandle`, `PerfAddCounters`, `PerfQueryCounterData`).
  - `pcwum` (`PcwCreateQuery`, `PcwAddQueryItem`, `PcwCollectData`).
  - The string "Processor Information".
  - PerfLib V2 reads the same counterset as PDH without the formatting layer. Its cost was not measured.
- **Cheapest correct per-tick set:** one PDH query holding `% Processor Utility(*)`, `% Privileged Utility(*)` and the single instance `\Processor Information(_Total)\% Processor Performance`. Cost: about 90–100 µs.

## Memory

Capture `02-Memory` (19:21:41Z), all from one tick:
- **Pane:** In use (Compressed) "38.0 GB (3.6 GB)", Available "25.3 GB", Committed "55/120 GB", Cached "22.6 GB", Paged pool "3.9 GB", Non-paged pool "3.4 GB".
- **Composition tooltips:** In use 38950 MB, in use compressed 3717 MB, Modified 297 MB, Standby 22875 MB, Free 3058 MB.
- **Totals:** 38950 + 297 + 22875 + 3058 = 65180 MB, which is total physical memory (65179.5 MB). The bar covers **total physical**, not installed memory.

| Task Manager value | Source | Value here | Match? | Elevation | Notes |
|---|---|---|---|---|---|
| Graph max "63.7 GB", sidebar denominator | `GlobalMemoryStatusEx.ullTotalPhys` (= `SystemBasicInformation.NumberOfPhysicalPages` × 4096) | 65179.5 MB = 63.65 GB | yes | no | |
| Title right "64.0 GB" | `GetPhysicallyInstalledSystemMemory` | 67108864 KB = 64.0 GB | yes | no | Equals the SMBIOS sum (2 × 32768 MB). |
| Hardware reserved "357 MB" | installed − total physical | 65536 − 65179.5 = 356.5 MB | yes | no | |
| In use "38.0 GB" | total − (Standby + Free + Zero + Modified), lists from `NtQSI(SystemMemoryListInformation = 80)` | 37.2 GB | yes (arithmetic exact against the tooltips) | no | Needed no privilege here. `PageCountByPriority[0..7]` summed = Standby. |
| Compressed "(3.6 GB)", tooltip "In use compressed (3717 MB)" | the Memory Compression process's working set: `WorkingSetSize` of the `SystemProcessInformation` entry named "Memory Compression", or `SystemStoreInformation` (109) / `MemCompressionInfoRequest` (22) `.WorkingSetSize` | 4009 MB and 4022 MB | plausible | no | `TotalCompressedSize` (4416 MB here) is the other candidate, about 10 % higher. Which one Task Manager uses is **unverified**. All `SM_MEM_COMPRESSION_INFO_REQUEST` sizes are **bytes**. |
| Tooltip "stores an estimated 12195 MB of data, saving the system 8478 MB" | estimate = `TotalUniqueDataCompressed`; saving = estimate − compressed | unique 12108 MB, total 14352 MB | plausible | no | 12195 − 3717 = 8478 exactly. That the estimate is the *unique* figure is **unverified**. Not in the spec's list; tooltip only. |
| Available "25.3 GB" | Standby + Free + Zero = `ullAvailPhys` = `SystemPerformanceInformation.AvailablePages` | 26.3 GB | yes (22875 + 3058 = 25933 MB → 25.3) | no | |
| Committed "55/120 GB" | `SystemPerformanceInformation.CommittedPages` / `.CommitLimit` × page size | 54.4 / 119.7 GB | plausible | no | Equal to `GetPerformanceInfo` CommitTotal/CommitLimit, which costs 12.6 ms. |
| Cached "22.6 GB" | Standby + Modified | 25.5 GB | yes (22875 + 297 = 23172 MB → 22.6) | no | **Not** `GetPerformanceInfo.SystemCache` (22.06 GB here). |
| Paged pool "3.9 GB" | `SystemPerformanceInformation.PagedPoolPages` | 3.906 GB | plausible | no | Equals `GetPerformanceInfo.KernelPaged` and PDH `Pool Paged Bytes`. `ResidentPagedPoolPage` (3.58) is the wrong one. |
| Non-paged pool "3.4 GB" | `SystemPerformanceInformation.NonPagedPoolPages` | 3.421 GB | plausible | no | |
| Sidebar "38.4/63.7 GB (60%)" | total − Available (= In use + Modified) | — | inferred: tooltips give 38.33 → "38.3"; shown "38.4" in the same capture | no | The pane said 38.0 in the same capture (at most one tick apart: the MSAA walk takes about 1.4 s and reads the sidebar first), so the sidebar isn't "In use". Modified explains 0.29 of the 0.35 GB gap, and the rest fits one tick of drift. **Unverified.** % = that value ÷ total. The memory graph's series is **unverified** (MSAA description "In use"). |
| Composition segments | In use; Modified = `ModifiedPageCount` (+ `ModifiedNoWritePageCount`, 0 here); Standby = Σ `PageCountByPriority`; Free = `FreePageCount` + `ZeroPageCount` | Zero 887–896, Free 11–258, Modified 210–422, Standby 25.7–25.9 GB | yes (sums to total) | no | Free must include Zero, or the four don't add up to total. |
| Hardware reserved segment | — | — | **mismatch with the brief** | — | The capture has 4 graphics (In Use, Modified, Standby, Free) and no Hardware-reserved segment, although the MUI has "Hardware reserved (%1!s! MB)". The bar sums to total physical. |
| Speed "5600 MHz" | SMBIOS type 17 **Configured Memory Speed** (offset 0x20) | 5600 (type 17 `Speed` at 0x15 is 4800) | yes | no | `GetSystemFirmwareTable('RSMB')` works non-elevated (SMBIOS 3.6). |
| Slots used "2 of 4" | type 16 `NumberOfMemoryDevices` (4); count of type 17 with Size ≠ 0 (2) | 2 of 4 | yes | no | Tooltip "Slot 1: empty / Slot 2: 32.0 GB, 5600 MHz / Slot 3: empty / Slot 4: 32.0 GB, 5600 MHz" follows type-17 table order (`DIMM 0`/`DIMM 1` × channels A/B). |
| Form factor "DIMM" | type 17 `FormFactor` 0x09 of a populated device | 0x09 | yes | no | Empty slots report 0x02 (Unknown); skip them. |

## Disk

What is actually attached:

| PhysicalDrive | Model (`ProductId`) | Bus | Partitions | Task Manager |
|---|---|---|---|---|
| 0 | CT1000MX500SSD1 | SATA (11) | GPT: LDM metadata `5808c8aa`, MSR, LDM data `af9b60a0` | Disk 0 (E:) |
| 1 | CT1000MX500SSD1 | SATA | same, **dynamic** | Disk 1 (E:) |
| 2 | CT1000MX500SSD1 | SATA | MSR + basic data D: | Disk 2 (D:) |
| 3 | Samsung SSD 990 PRO 2TB | NVMe (17) | MSR + F: | Disk 3 (F:) |
| 4 | Sabrent Rocket Q | NVMe | EFI, MSR, C:, Recovery | Disk 4 (C:) |
| 5 | Msft Virtual Disk, 8 GB | FileBackedVirtual (15) | MBR, one NTFS volume mounted at `C:\ProgramData\Microsoft\Windows\Containers\BaseImages\…\BaseLayer` | not listed |

- **E: is not a Storage Spaces volume.** E: "mx500 x2" (2 TB NTFS, volume GUID `{56fda4ba-6480-11ef-…}`) is an LDM dynamic volume (striped or spanned) with one 931.386 GiB extent on each of disks 0 and 1.
  - The storage pool is only the primordial one, and there are no virtual disks.
  - `Get-PhysicalDisk` shows three distinct MX500s, not duplicates.
  - `Get-Disk` hides the dynamic disks 0 and 1.

| Task Manager value | Source | Value here | Match? | Elevation | Notes |
|---|---|---|---|---|---|
| Which disks, "Disk N" | see "Disk listing rule" | 0–4 | yes | no | |
| "(E:)" on Disk 0 and Disk 1 | volumes (`FindFirstVolumeW`/`FindNextVolumeW`) → open `\\?\Volume{…}` with access 0 → `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS` → disk numbers; letter from `GetVolumePathNamesForVolumeNameW` | E: on 0 and 1, D: 2, F: 3, C: 4 | yes | no | PDH instance names are `0`, `1`, `2 D:`, `3 F:`, `4 C:`, `5`: they carry no E: for dynamic disks, so don't build labels from them. MUI: "Disk %1!d! (%2!s!)", or "Disk %d" when there's no letter. Separator for several letters is **unverified**. |
| Model "Sabrent Rocket Q" (title right) | `IOCTL_STORAGE_QUERY_PROPERTY(StorageDeviceProperty)` `ProductId`, trimmed; or `DEVPKEY_Device_FriendlyName` | identical for all 5 disks; `VendorId` empty | yes | no | Taskmgr imports `SetupDiGetDevicePropertyW`, so FriendlyName is likely. **Unverified**, since the two never differ here. |
| Active time "1%" / "0%" | 1 − ΔIdleTime ÷ ΔQueryTime from `IOCTL_DISK_PERFORMANCE` on `\\.\PhysicalDriveN` opened with access 0 | 85.9 % under a write load | equal to PDH 100 − `% Idle Time` (85.9) | no | **Not** PDH `% Disk Time` (32.4 in the same interval). Clamp to 0–100. Equivalence to Task Manager **unverified** (it was idle in the capture). |
| Average response time "1.6 ms" | Δ(ReadTime + WriteTime) ÷ Δ(ReadCount + WriteCount), 100 ns units | 0.21 ms | equal to PDH `Avg. Disk sec/Transfer` (0.21 ms) | no | "0 ms" when there was no I/O. |
| Read speed "0 KB/s" / Write speed "1.3 MB/s" | ΔBytesRead, ΔBytesWritten ÷ Δt (`QueryTime` delta) | 2 KB/s / 708 MB/s under load | equal to PDH `Disk Read/Write Bytes/sec` | no | |
| Capacity "932 GB", "1.8 TB" | `IOCTL_DISK_GET_DRIVE_GEOMETRY_EX.DiskSize` (access 0) | 1000204886016 B = 931.51 GiB; 2000398934016 B = 1.819 TiB | yes | no | `IOCTL_DISK_GET_LENGTH_INFO` fails with error 5 (needs read access, i.e. elevation). |
| Formatted "931 GB" (disks 0, 1), "932 GB" (disk 2), "1.8 TB" (3, 4) | Σ volume extent lengths on that disk (`IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`) | 931.386, 931.386, 931.496, 1863.000, 1862.999 GiB | yes, with the double rounding in "Formats" | no | Partition sums fail on disks 0 and 1 (931.512 → "932"). Disk 2's 931.496 shows "932", which only fits if the number is first rounded to 931.5 (**inferred**). Whether hidden volumes (EFI, Recovery) count can't be told at TB precision. |
| System disk "Yes" (disk 4) | disk holding the volume of `GetSystemWindowsDirectoryW` (C:) | disk 4 | yes | no | Disk 4 also holds the EFI system partition, so the two rules can't be told apart here. **Unverified.** |
| Page file "Yes" (disk 4) | `NtQSI(SystemPageFileInformation = 18)` names (`\??\C:\pagefile.sys`), or registry `…\Session Manager\Memory Management\ExistingPageFiles` → letter → extents | C: → disk 4 | yes | no | Taskmgr contains the `ExistingPageFiles` string. Both work non-elevated. |
| Type "SSD" | `IOCTL_STORAGE_QUERY_PROPERTY(StorageDeviceSeekPenaltyProperty)`: `IncursSeekPenalty` 0 → "SSD", 1 → "HDD" | 0 on all | yes | no | MUI also has "Removable". The rule for it (`RemovableMedia`? bus type?) is **unverified**. |
| Sidebar "SSD", "0%" | type and active time | | yes | no | |

### Disk listing rule

1. Enumerate **present** `GUID_DEVINTERFACE_DISK` interfaces with `CM_Get_Device_Interface_ListW(…, CM_GET_DEVICE_INTERFACE_LIST_PRESENT)` or `SetupDiGetClassDevsW(DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)`.
2. Open each interface path with access 0 and read `IOCTL_STORAGE_GET_DEVICE_NUMBER`.
3. Sort by `DeviceNumber`. Name each "Disk N", N = device number = PhysicalDrive number.

Here this gives 0–4. Drive 5 (the containers VHDX) has only a non-present interface, and opening it fails with error 2. Yet `\\.\PhysicalDrive5` opens and PDH has an instance "5", so neither PDH nor probing PhysicalDrive0..N reproduces the list. `Win32_DiskDrive` lists 0–4 too. Excluding `BusType == 15` would also drop drive 5; which rule Task Manager applies is **unverified**. The interface rule matches Taskmgr's `SetupDiGetClassDevsW`/`SetupDiEnumDeviceInfo` imports.

## Network

| Task Manager value | Source | Value here | Match? | Elevation | Notes |
|---|---|---|---|---|---|
| Which adapters, and their order | see "Adapter listing rule" | the 12, same order | yes | no | |
| Title "Ethernet" / "Wi-Fi" | `IfType == 71` (IEEE 802.11) → "Wi-Fi", otherwise "Ethernet" | Wi-Fi 2 is 71; Tailscale (53) and the rest give "Ethernet" | yes | no | MUI also has "Bluetooth PAN" and "Wi-Fi Direct". Their rules are **unverified**. |
| Subtitle "Tailscale Tunnel", "Hyper-V Virtual Ethernet Adapter #8" | GAA `Description` | same | yes | no | |
| Sidebar line 2 and "Adapter name" | GAA `FriendlyName` (alias) | same | yes | no | |
| DNS name "taila1d03.ts.net", "home.arpa" | GAA `DnsSuffix`; the row is **omitted** when it's empty (no row on any vEthernet pane) | same | yes | no | Don't pass `GAA_FLAG_SKIP_DNS_SERVER` (0x8): it blanks Tailscale's suffix (checked twice). |
| Connection type "Ethernet" / "802.11ax" | non-Wi-Fi → "Ethernet". Wi-Fi → `WlanQueryInterface(wlan_intf_opcode_current_connection)`, `wlanAssociationAttributes.dot11PhyType`: 4 a, 5 b, 6 g, 7 n, 8 ac, 9 ad, 10 ax | 10 → 802.11ax | yes | no | Taskmgr.exe itself holds exactly the strings 802.11a/ac/ad/ax/b/g/n. |
| SSID "frame_4BD93CDF" | same struct, `dot11Ssid` | frame_4BD93CDF | yes | no | Taskmgr imports `WlanUtf8SsidToDisplayName`. On Windows 10, wlanapi needs no location consent. |
| Signal strength (bars) | `wlanAssociationAttributes.wlanSignalQuality` (0–100) | 60 | **unverified** | no | MSAA shows an empty text with a 16×19 image. The quality-to-bars mapping is unknown. The `rssi` opcode returned 0. |
| IPv4 address "10.24.200.9" | first IPv4 entry in GAA `FirstUnicastAddress` | same for all 12 | yes | no | |
| IPv6 address "fd7a:115c:a1e0::d301:810e", "fe80::38b8:b061:af62:4d58%76" | first IPv6 entry in GAA `FirstUnicastAddress`, formatted with its scope ID (`%ifIndex`) when non-zero | same for all 12 | yes | no | Tailscale lists fd7a:… before its fe80:…, so "first" and "prefer non-link-local" can't be told apart: **unverified**. Taskmgr imports `RtlIpv6AddressToStringExW`. |
| Send "40.0 Kbps" / Receive "16.0 Kbps" | Δ`OutOctets` / Δ`InOctets` from `GetIfEntry2` (row keyed by `InterfaceLuid`) × 8 ÷ Δt | NC550SFP Port 2: 16.7 / 36.5 Kbps | plausible | no | Bits per second. See the rounding note in "Formats". |
| Graph scale "100 Kbps", "500 Kbps" | — | — | **unverified** | — | See "Formats". |

### Adapter listing rule

1. Call `GetAdaptersAddresses(AF_UNSPEC, flags = 0)` (or flags without `SKIP_DNS_SERVER`). It returned 22 IP-bound interfaces.
2. Keep entries with `OperStatus == IfOperStatusUp (1)` and `IfType != IF_TYPE_SOFTWARE_LOOPBACK (24)`.
3. **Keep GAA's own list order.**

Result here: Tailscale, vEthernet (Default Switch), NC550SFP Port 2 (2695v2), vEthernet (WSL), Wi-Fi 2, vEthernet (Wi-Fi), vEthernet (Intel I225-V), vEthernet (NC550SFP Port 1), vEthernet (NC550SFP Port 2), vEthernet (Ethernet 2), vEthernet (Ethernet 3), vEthernet (Wi-Fi 2). That is exactly Task Manager's list and order.

- **No `MIB_IF_ROW2` rule exists.** `vSwitch (Default Switch)` (not listed) and `vEthernet (Default Switch)` (listed) are identical in every field checked: Type 6, TunnelType 0, MediaType 0, PhysicalMediumType 0, AccessType 2, no InterfaceAndOperStatusFlags (so no HardwareInterface, FilterInterface, ConnectorPresent or EndPointInterface), OperStatus Up, AdminStatus Up, MediaConnectState Connected, ConnectionType 1. What separates them is the TCP/IP binding, i.e. being present in `GetAdaptersAddresses`.
- **Excluded:** 9 Down adapters (NC550SFP Port 1, Intel I225-V, Ethernet 2, Ethernet 3, Wi-Fi, two Wi-Fi Direct, the TAP and OpenVPN DCO adapters) and the loopback.
- **Order key:** it is **not** ifIndex (73, 76, 12, 118, 52, 113, …). It is **not** LUID, which is `GetIfTable2`'s order and would put the vEthernets before NC550SFP Port 2 and Tailscale last. It is **not** `Tcpip\Linkage\Bind`, which starts NC550SFP Port 2, Tailscale, Default Switch, vEth (Intel I225-V).
  - GAA's order comes from TCP/IP's interface table. Its underlying key is **unverified**, so just preserve GAA order.
  - Whether it stays stable across reboots or adapter changes is **unverified**.
- **Don't enumerate with `GetIfTable2`** (135 rows). It also returns NDIS filter instances (`…-WFP Native MAC Layer LightWeight Filter-0000`, Npcap, QoS) and the WAN Miniports (IP, IPv6, Network Monitor). It also returns the "Hyper-V Virtual Switch Extension Adapter" vSwitches, which are Up but not IP-bound.
- **Don't use .NET's `NetworkInterface.GetAllNetworkInterfaces()`.** It returns those extra rows too, so the filter above would let them through.
- **Refresh:**
  - Taskmgr imports `GetAdaptersAddresses`, `GetIfEntry2` and `NotifyUnicastIpAddressChange`, and **not** `GetIfTable2`.
  - Suggested: rebuild the list on `NotifyIpInterfaceChange` / `NotifyUnicastIpAddressChange`, or every few seconds (4.4 ms per call), and poll `GetIfEntry2` per listed adapter each tick.
  - How Task Manager notices an adapter going Up or Down is **unverified**.

## Formats

- **Sizes are binary** (GB = 2^30, TB = 2^40): 1000204886016 B shows "932 GB", 65179.5 MB shows "63.7 GB". MUI units: "%s MB", "%s GB", "%s TB", "%s PB".
- **Decimals:**
  - One decimal below 100 ("38.0 GB", "3.6 GB", "1.8 TB", "1.3 MB/s", "1.6 ms", "16.0 Kbps").
  - None at 100 or above ("932 GB", "357 MB", "120 Kbps", "216 Mbps").
  - Exactly zero shows "0" ("0 KB/s", "0 Kbps", "0 ms").
- **Rounding:** half-up, possibly applied twice (to one decimal first, then to the displayed precision). Disk 2's 931.496 GiB shows "932". All other captured values fit single rounding too. **Inferred** from one case.
- **Pairs share precision**, set by the larger value: "55/120 GB" (54.7/119.7), "38.4/63.7 GB (60%)". MUI: "%1!s!/%2!s! GB" and "%1!s! (%2!s!)" for In use (Compressed).
- **CPU:** Speed has two decimals ("4.43 GHz"; MUI "%s GHz"). Utilization is an integer "%". Up time is `d:hh:mm:ss` with unpadded days ("25:17:28:41").
- **Memory static values:** "%u MHz", "%1!u! of %2!u!", "Slot %1!u!: %2!s!, %3!hu! MHz", "Slot %1!u!: empty".
- **Network rates:**
  - Units are bps, Kbps, Mbps and Gbps.
  - Every Kbps value in the captures is a multiple of 8 (8, 16, 24, 40, 56, 64, 72, 120, 144, 200, 360, 376, 392). So Task Manager seems to turn the byte delta into whole kilobytes per second before multiplying by 8. Whether K is 1000 or 1024 is **unverified**.
  - **Sidebar:** "S: x R: y unit" with **one shared unit** chosen by the larger value: "S: 8.0 R: 40.0 Kbps", "S: 0 R: 8.0 Kbps", "S: 0.3 R: 21.7 Mbps", "S: 1.4 R: 216 Mbps".
  - **Pane:** Send and Receive each get their own unit.
- **Disk rates** are "KB/s" and "MB/s" (MUI "%s MB/s").
- **Graph scale captions (MUI string set):**
  - Network: 10 Kbps, 100 Kbps, 500 Kbps, 1 Mbps, 10 Mbps, 100 Mbps, 1 Gbps, 10 Gbps, 100 Gbps; also 450 Kbps, 800 Kbps, 7.7, 11, 32, 50, 54, 200, 300 and 500 Mbps, and 2 Gbps.
  - Disk: 100 KB/s, 500 KB/s, 1 MB/s, 10 MB/s, 100 MB/s, 250 MB/s, 500 MB/s, 1 GB/s, 10 GB/s, 100 GB/s; also 450 KB/s, 800 KB/s, 7, 60, 125, 200 and 300 MB/s, and 2 GB/s.
- **Graph scales seen:**
  - 100 Kbps on idle adapters.
  - 500 Kbps on NC550SFP Port 2 at 19:22:07 while it showed 40/16 Kbps.
  - 100 KB/s on idle disks, 10 MB/s on C: at 1.3 MB/s.
- **Scale selection rule: unverified.** The 60-second maximum doesn't explain it: the sidebar showed NC550SFP Port 2 receiving 323 Mbps at 19:21:58, 9 s before its pane showed a 500 Kbps scale. The scale may follow recent samples only, or history may restart when a pane is shown.

## Bounded-memory notes (spec 5)

All of these can run on fixed buffers:
- The PDH formatted arrays: the CPU and disk instance sets are static (PhysicalDisk changes only on hot-plug).
- The `NtQSI` buffers (classes 2, 3, 80 and 109 are fixed-size).
- One `MIB_IF_ROW2` per adapter.
- One `DISK_PERFORMANCE` per disk, with the disk handles kept open (6 here).

`GetAdaptersAddresses` and the disk and volume enumeration run only on change. Avoid `GetPerformanceInfo` on the tick path: it costs 12.6 ms.
