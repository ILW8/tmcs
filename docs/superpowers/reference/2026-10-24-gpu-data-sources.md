# GPU 0 data sources: how to reproduce Task Manager's GPU pane

Verified on 2026-10-24, 19:38–20:00Z (local time is UTC+1; clock times below are local), on the owner's PC: Windows 10 Pro 19045, RTX 3090, driver 32.0.16.1088, **hardware-accelerated GPU scheduling (HAGS) on**. Every probe ran **non-elevated**. The probe is a scratch .NET 10 console app (not in the repo). Struct offsets were dumped by compiling the Windows SDK headers with clang for x64 (`offsetof`/`sizeof`). Ground truth is the 19:25Z capture `artifacts/reference/taskmgr/20-GPU 0.txt`.

## Verdict

- **Utilization can't come from global D3DKMT node statistics on this machine.** With HAGS on, `D3DKMT_QUERYSTATISTICS_NODE` reports RunningTime **0** for node 0 (3D) and node 4 (Copy) while PDH shows 3D at 2–10 %. Only software-scheduled or system work accumulates globally (video nodes, the System process's paging copies on node 9). Two sources work:
  - **(A) PDH `\GPU Engine(*)\Running Time`**, raw counter array. This is what Task Manager itself uses. Needs no elevation. Memory stayed flat under process churn (see "Cost and boundedness").
  - **(B) `D3DKMT_QUERYSTATISTICS_PROCESS_NODE`** per process, summed. PDH's value is exactly this sum (pid 32236, node 0: 70409164571 from both). It needs a `PROCESS_QUERY_INFORMATION` handle to every process.
    - Non-elevated it misses the biggest GPU users here: WUDFHost (pid 1792, the indirect-display driver host), csrss, dwm and System. It showed 3 % against PDH's 7 %.
    - csrss is a protected process, so even elevated it should deny `PROCESS_QUERY_INFORMATION` (inferred from PPL rules, not tested).
  - **Recommendation:** (A).
- **Everything else comes from D3DKMT, SetupAPI and the registry**, all non-elevated and cheap. The one exception is the DirectX version, which also has a cheap registry path.
- **Task Manager's own sources**, from `Taskmgr.exe` imports and strings, cross-check every row below:
  - PDH: `\GPU Engine(*)\Running Time` and `Utilization Percentage`, `\GPU Adapter Memory(*)\Dedicated Usage`, `Shared Usage` and `Total Committed`, `\GPU Local Adapter Memory`, `\GPU Non Local Adapter Memory`, `\GPU Process Memory`.
  - `D3DKMTOpenAdapterFromLuid` / `D3DKMTQueryAdapterInfo` / `D3DKMTCloseAdapter`. It does not import `D3DKMTQueryStatistics` or `D3DKMTEnumAdapters*`.
  - `DXCoreCreateAdapterFactory`, `CreateDXGIFactory2`, `D3D12CreateDevice` (ordinal 101), `D3D11CreateDevice`.
  - `SetupDiGetClassDevsW` / `SetupDiEnumDeviceInfo` / `SetupDiGetDevicePropertyW`.
  - Format string `%s (FL %s)` and engine labels `Copy`, `Video Decode`, `Video Encode`, `Video Processing`, `Legacy Overlay`, `Crypto`, `Compute`, plus `engtype_`.

## Value table

| Task Manager value | Source | Value here | Match? | Elevation | Notes |
|---|---|---|---|---|---|
| Title / sidebar "NVIDIA GeForce RTX 3090" | `KMTQAITYPE_DRIVER_DESCRIPTION` (65), or `ADAPTERREGISTRYINFO` (8) `.AdapterString` | NVIDIA GeForce RTX 3090 | yes | no | Type 8 fails (`0xC0000034`) on IDD and software adapters; type 65 works everywhere. |
| "GPU 0" | index in the filtered adapter list (see listing rule) | only one adapter qualifies | n/a | no | Numbering across several real GPUs is **unverified** (one GPU here). |
| Utilization 14 % | max over **all** nodes of per-node % ("Highest utilization across all GPU engines") | 3D 2–10 % at 20:41–20:44 (GPU near idle) | plausible | no (PDH) | See "Utilization". |
| 3D / Copy / Video Encode / Video Decode % | per-node % from PDH `Running Time` deltas, grouped by engine type | 3D = node 0, Video Decode = node 2, Video Encode = node 6, Copy = nodes 3, 4, 9–12 | plausible | no | The capture's Video Decode 12 % was a video playing at 19:25; node 2 is idle now. How Copy combines six nodes is **unverified**. |
| Dedicated GPU memory total "24.0 GB" | registry `HardwareInformation.qwMemorySize` in the adapter's class key | 25,769,803,776 B = 24.000 GiB | yes | no | **Not** `DedicatedVideoMemorySize` (25,503,465,472 = 23.75 GiB, which would show "23.8 GB"). |
| Shared GPU memory total "31.8 GB" | `KMTQAITYPE_GETSEGMENTSIZE` (3) `.SharedSystemMemorySize` | 34,172,811,264 B = 31.826 GiB | yes | no | Same as DXGI `SharedSystemMemory`. |
| GPU Memory total "55.8 GB" | qwMemorySize + SharedSystemMemorySize | 55.826 GiB | yes | no | DedicatedVideoMemorySize + Shared would give 55.6. |
| Hardware reserved memory "254 MB" | qwMemorySize − `DedicatedVideoMemorySize` | 266,338,304 B = 254.0 MiB | yes | no | |
| Dedicated used "3.0 GB" | sum of `BytesResident` over segments with `Aperture == 0` (`D3DKMT_QUERYSTATISTICS_SEGMENT`); here only segment 1 | 3,119,497,216 B = 2.905 GiB at 20:54 | **identical to the byte** to PDH `GPU Adapter Memory\Dedicated Usage`, read in the same second | no | Task Manager reads that PDH counter. Its instances are per adapter, so they don't churn. Not `BytesCommitted`: that equals PDH `GPU Local Adapter Memory\Local Usage` (1.0 GiB). |
| Shared used "0.3 GB" | sum of `BytesResident` over segments with `Aperture == 1` (segments 0 and 2) | 219,668,480 B = 0.205 GiB | **identical** to PDH `GPU Adapter Memory\Shared Usage` | no | Aperture `BytesCommitted` equals `GPU Non Local Adapter Memory\Non Local Usage`. |
| GPU Memory used "3.3 GB" | dedicated used + shared used | 2.905 + 0.205 = 3.11 GiB at 20:54; 3.35 at 20:40 | plausible | no | The 19:25 capture showed 3.0 + 0.3 = 3.3. |
| GPU Temperature "39 °C", sidebar "(39 °C)" | `KMTQAITYPE_ADAPTERPERFDATA` (62) `.Temperature`, in deci-°C; or `D3DKMT_QUERYSTATISTICS_PHYSICAL_ADAPTER` (10) | 378 → 37.8 °C | plausible | **no** | Works non-elevated. Rounding vs truncation **unverified**. Fails (`0xC000000D`) on the Basic Render Driver; hide the value then. |
| Driver version "32.0.16.1088" | `DEVPKEY_Device_DriverVersion` (SetupAPI), or registry `DriverVersion` in the class key, or `KMTQAITYPE_UMD_DRIVER_VERSION` (18) as four 16-bit fields | 32.0.16.1088 (all three) | yes | no | |
| Driver date "7/22/2026" | `DEVPKEY_Device_DriverDate` (FILETIME, 2026-07-22 00:00 UTC), shown in the user's short-date format | `M/d/yyyy` (en-US) gives 7/22/2026 | yes | no | Registry `DriverDate` is "7-22-2026". Locale formatting vs dash-to-slash can't be told apart here: **unverified**. Use `DateTime.ToString("d")` on the FILETIME read as UTC. |
| DirectX version "12 (FL 12.1)" | max D3D12 feature level: `D3D12CreateDevice(adapter, FL, IID_ID3D12Device, NULL)` from 12_1 down; first S_FALSE wins | 12_2 → E_INVALIDARG, 12_1 → S_FALSE | yes | no | Cheaper: registry `HKLM\SOFTWARE\Microsoft\DirectX\{AdapterUniqueGuid}\MaxD3D12FeatureLevel` = 0xC100. See "Static values". |
| Physical location "PCI bus 1, device 0, function 0" | `DEVPKEY_Device_LocationInfo` (SetupAPI), verbatim | PCI bus 1, device 0, function 0 | yes | no | `KMTQAITYPE_ADAPTERADDRESS` (6) gives bus 1, dev 0, func 0 for formatting yourself. "PCI bus" is not in `Taskmgr.exe.mui`, so Task Manager shows the PnP string. |

## Adapter listing rule

`D3DKMTEnumAdapters2` returns 6 adapters here. `ADAPTERTYPE` is `KMTQAITYPE_ADAPTERTYPE` (15); bits: 0 RenderSupported, 1 DisplaySupported, 2 SoftwareDevice, 3 PostDevice, 6 IndirectDisplayDevice, 8 ACGSupported, 9 SupportSetTimingsFromVidPn, 11 ComputeOnly.

| LUID | Description (type 65) | ADAPTERTYPE | In EnumAdapters3(Filter=0)? |
|---|---|---|---|
| 0x1D8C7 | NVIDIA GeForce RTX 3090 | 0x30B Render, Display, Post, ACG, SetTimings | yes |
| 0x207C4 | Microsoft Basic Render Driver | 0x105 Render, **Software**, ACG | yes |
| 0x8D634, 0x72E37 | DisplayLink USB Device | 0x142 Display, **Indirect**, ACG | no |
| 0x2B1B2, 0x2ACD0 | Parsec Virtual Display Adapter | 0x142 Display, **Indirect**, ACG | no |

"Virtual Desktop Monitor" (`ROOT\DISPLAY\0002`) is a display-class PnP device but not a D3DKMT adapter, so no enumerator returns it.

**Rule:** `D3DKMTEnumAdapters3` with `Filter = 0` (which drops display-only and compute-only adapters), keeping adapters whose ADAPTERTYPE has `RenderSupported` and not `SoftwareDevice`. Equivalently, from EnumAdapters2: Render, not Software, not IndirectDisplay. Result: exactly the RTX 3090.

DXCore lists with `D3D12_CORE_COMPUTE`, `D3D12_GRAPHICS` or `D3D11_GRAPHICS` give the same answer with `IsHardware == 1`. Task Manager imports DXCore, so this is probably its list (**unverified**).

Traps:
- DXGI `EnumAdapters1` returns the **RTX five times**: its own LUID plus one per IDD LUID. Each copy has the same description and memory sizes.
- `D3DKMTQueryStatistics` and `D3DKMTQueryAdapterInfo` on an **IDD LUID or handle silently return the RTX's data**: same 13 nodes, segments, temperature and PnP key. Filter by ADAPTERTYPE, never by "has nodes" or "has memory".
- Every `hAdapter` returned by EnumAdapters2/3 is open. Close each one you don't keep with `D3DKMTCloseAdapter`.
- Compute-only (MCDM) adapters: `Taskmgr.exe.mui` contains "Compute accelerator", "Compute accelerator activity" and "Compute %d", so this Task Manager build would list them, as "Compute N" rather than "GPU N". None exist here. Keep `Filter = 0`: the spec wants real GPUs only.

## Engines (nodes)

`D3DKMT_QUERYSTATISTICS_ADAPTER` gives NodeCount = 13 and NbSegments = 3. For each node, call `KMTQAITYPE_NODEMETADATA` (25) with `NodeOrdinalAndAdapterIndex = node` (the physical adapter index is in the high word).

| Node | DXGK_ENGINE_TYPE | FriendlyName | PDH `engtype_` | Task Manager label |
|---|---|---|---|---|
| 0 | 3D (1) | "" | 3D | **3D** |
| 1 | OVERLAY (7) | "" | LegacyOverlay | Legacy Overlay |
| 2 | VIDEO_DECODE (2) | "" | VideoDecode | **Video Decode** |
| 3, 4, 9, 10, 11, 12 | COPY (6) | "" | Copy | **Copy** |
| 5 | OTHER (0) | Security | Security | Security |
| 6 | VIDEO_ENCODE (3) | "" | VideoEncode | **Video Encode** |
| 7 | OTHER (0) | OFA_0 | OFA_0 | OFA_0 |
| 8 | OTHER (0) | VR | VR | VR |

- Labels: map DXGK_ENGINE_TYPE to a fixed string (3D, Video Decode, Video Encode, Video Processing, Scene Assembly, Copy, Legacy Overlay, Crypto); for OTHER, use FriendlyName. PDH's `engtype_` suffix carries the same string without spaces.
- All of these labels except "Scene Assembly" appear as strings in `Taskmgr.exe`. "Scene Assembly" is inferred.
- Typed nodes have an empty FriendlyName. NVIDIA reports names only for OTHER nodes.
- PDH engine index `eng_N` equals the D3DKMT node ordinal.
- **Copy graph:** six nodes share the type. Task Manager's graph is single and labelled "Copy". Whether it shows the max or the sum of those nodes is **unverified**: only nodes 4 and 9 were ever active, at ≤ 0.6 %. Take the **max** over same-type nodes; that matches the "highest engine" convention of the Utilization number.
- Overall Utilization is the max over all 13 nodes, including Security, OFA and VR. Task Manager's string: "Highest utilization across all GPU engines".

## Utilization

Per node: % = ΔRunningTime / Δwall × 100, both in **100 ns units**.
- The header comment says "micro-second". The data says 100 ns: global node 9 = 40,338,927,219 against PDH `Running Time` for pid 4 / eng 9 = 40,340,853,492. The percentages computed in 100 ns units track `Utilization Percentage`.
- PDH `Running Time` is a raw counter. Use `FirstValue` as the running time and `RawValue.TimeStamp` (FILETIME, 100 ns) as the time base.

Global node statistics (`D3DKMT_QUERYSTATISTICS_NODE`) on this machine, sampled at 1 s for 12 s:
- node 0 3D: RunningTime 0 throughout, while PDH reported 1.8–9.6 %.
- node 9 Copy: 0.0–2.3 %, matching PDH's node 9.
- node 2 Video Decode and node 6 Video Encode: large static totals (708,695,658,141 and 566,029,556,810). These are software scheduled and count globally.

`WDDM_2_7_CAPS` (70) = 0xB: HwSchSupported, HwSchEnabled, IndependentVidPnVSyncControl. With HAGS off, global node statistics would probably work (**unverified**; it can't be turned off here without a reboot and a settings change). tmcs must not rely on them.

**(A) PDH: Task Manager's method.**
- `PdhOpenQueryW`, then `PdhAddEnglishCounterW("\GPU Engine(*)\Running Time")`, then per tick `PdhCollectQueryData` and `PdhGetRawCounterArrayW` into a reused buffer. Grow it on `PDH_MORE_DATA` (0x800007D2).
- Instance names look like `pid_1234_luid_0x00000000_0x0001D8C7_phys_0_eng_0_engtype_3D`. Keep instances of the adapter's LUID. Sum Δ`FirstValue` per `eng_N` over instances present in both samples, then divide by ΔTimeStamp.
- Skip items whose `CStatus` isn't valid: exiting processes produce invalid-data items mid-churn.
- Keep the previous-sample map keyed by instance name, rebuilt each tick so it holds only live instances.
- Computed raw values matched `Get-Counter` and the formatted `Utilization Percentage` array tick for tick.
- 494–520 instances (38+ processes × 13 nodes); about 0.7 ms per tick in-process.

**(B) Per-process D3DKMT.**
- For each process: `D3DKMT_QUERYSTATISTICS_PROCESS_NODE` (6) with `hProcess`, `AdapterLuid` and `QueryProcessNode.NodeId`, giving RunningTime at result +0.
- **It needs `PROCESS_QUERY_INFORMATION` (0x400).** With `PROCESS_QUERY_LIMITED_INFORMATION` every call fails with `STATUS_INVALID_PARAMETER` (0xC000000D), not ACCESS_DENIED.
- Processes with no GPU context also fail with 0xC000000D on the first node, which acts as a free prefilter.
- Non-elevated: about 260 opened and 227 denied. Lifetime running time (100 ns) of the top users, from PDH:

  | Process | Running time | Opened non-elevated? |
  |---|---|---|
  | WUDFHost, pid 1792 (indirect-display driver host) | 131 G | no |
  | vivaldi | 97 G | yes |
  | WindowsTerminal | 71 G | yes |
  | System | 40 G | no |
  | csrss | 32 G | no |
  | dwm | 17 G | no |

  That is why the sum showed 3 % against PDH's 7 %.
- Elevated, WUDFHost and dwm are probably openable. csrss is PPL and denies 0x400 even to admins. **Unverified** (not run elevated).
- Cost, with 250 cached handles and 26 GPU processes: about 562 calls and 0.4–0.5 ms a tick. Opening handles costs extra; in the probe it hid inside the 10 ms `Process.GetProcesses()`.
- The handles must be held per process identity and closed at exit.

## Static values

- **Driver version, date, location.** `SetupDiGetClassDevsW(GUID_DEVCLASS_DISPLAY {4d36e968-e325-11ce-bfc1-08002be10318}, NULL, NULL, DIGCF_PRESENT)`, then `SetupDiEnumDeviceInfo`, then `SetupDiGetDevicePropertyW`:
  - `DEVPKEY_Device_LocationInfo` {a45c254e-df1c-4efd-8020-67d146a850e0},15 (string)
  - `DEVPKEY_Device_BusNumber` …,23 (UINT32) = 1
  - `DEVPKEY_Device_Address` …,30 (UINT32) = (device << 16) | function = 0
  - `DEVPKEY_Device_DriverVersion` {a8b865dd-2e3d-4094-ad97-e593a70c75d6},3 (string)
  - `DEVPKEY_Device_DriverDate` …,2 (FILETIME)
  - **Join to the D3DKMT adapter:** BusNumber/Address equal `ADAPTERADDRESS` (bus 1, dev 0, func 0). Or compare instance paths: `KMTQAITYPE_PHYSICALADAPTERPNPKEY` (41) with `D3DKMT_PNP_KEY_HARDWARE` returns `\REGISTRY\MACHINE\SYSTEM\ControlSet001\Enum\PCI\VEN_10DE&DEV_2204&SUBSYS_147D10DE&REV_A1\4&1babdf5b&0&0009\Device Parameters`.
  - The DisplayLink devices also report bus 0 / addresses 2–3, so match on the **RTX's** address, not on "has a location".
- **Physical VRAM (for 24.0 GB and 254 MB).** `KMTQAITYPE_PHYSICALADAPTERPNPKEY` (41) with `D3DKMT_PNP_KEY_SOFTWARE` returns `\REGISTRY\MACHINE\SYSTEM\ControlSet001\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000`. Map it to `HKLM\SYSTEM\ControlSet001\...`, or use the `Driver` property `{4d36e968…}\0000` under `HKLM\SYSTEM\CurrentControlSet\Control\Class\`. Read `HardwareInformation.qwMemorySize` (REG_QWORD) = 25,769,803,776.
  - The DWORD `HardwareInformation.MemorySize` is capped at 4,293,918,720; don't use it.
  - Where Task Manager itself gets the physical size is **unverified**; this registry value reproduces all three numbers exactly.
- **DirectX version.** Task Manager probes with `D3D12CreateDevice` and `D3D11CreateDevice` and formats `"%s (FL %s)"`. Measured here:
  - `D3D12CreateDevice(adapter, minFL, IID_ID3D12Device, NULL)`: 12_2 → `E_INVALIDARG` (the in-box 19045 runtime doesn't know FL 12_2); 12_1, 12_0, 11_1, 11_0 → `S_FALSE`.
  - A real device's `CheckFeatureSupport(FEATURE_LEVELS)` with 12_2 in the list → MaxSupportedFeatureLevel 0xC100.
  - `D3D11CreateDevice(adapter, UNKNOWN, levels 12_1…9_1, ppDevice=NULL)` → 0xC100.
  - The string is "12" when D3D12 succeeds (else "11" from D3D11) plus "(FL major.minor)" from `FL >> 12` and `(FL >> 8) & 0xF`.
  - Expect "12 (FL 12.2)" on Windows 11 for the same card.
  - **Cost** in a fresh process:

    | Step | Private bytes | Modules |
    |---|---|---|
    | DXGI factory + enumeration | +0.3 MB | +1 |
    | D3D12 NULL-device checks | +1.4 MB | +23 (the NVIDIA UMD gets mapped) |
    | D3D11 NULL check | +0.7 MB | |
    | A real D3D12 device, while alive | **+72 MB** | |
    | After releasing everything | still **+25 MB** | 69 vs 39 at start; DLLs stay loaded |

    So: never create a real device. A NULL-device check once at startup costs about 2.5 MB that is never returned.
  - **Cheaper, no DLLs:** `KMTQAITYPE_QUERY_ADAPTER_UNIQUE_GUID` (60) returns `{B5DDE86D-96A6-11F1-9016-00249B7B0E5D}`. That is the subkey `HKLM\SOFTWARE\Microsoft\DirectX\{B5DDE86D-…}`, which holds `AdapterLuid` = 121031 (0x1D8C7), `MaxD3D12FeatureLevel` = 0xC100 and `MaxD3D11FeatureLevel` = 0xC100.
    - The cache is maintained by the DirectX adapter-cache updater, and its format is undocumented. It also holds about 20 stale entries; match on the unique GUID or `AdapterLuid`, never on Description.
    - Fall back to the NULL-device probe when the key or value is missing.

## Struct layouts (x64, verified)

From clang `offsetof`/`sizeof` on SDK 10.0.22621 headers. Identical under 10.0.19041 for every struct present in both; 19041's public `d3dkmthk.h` lacks the QUERYSTATISTICS types. Each call's success here is further evidence: dxgkrnl rejects a wrong `PrivateDriverDataSize`.

```
D3DKMT_ADAPTERINFO        20  hAdapter u32 @0, AdapterLuid @4 (LowPart u32, HighPart i32), NumOfSources u32 @12, BOOL @16
D3DKMT_ENUMADAPTERS2      16  NumAdapters u32 @0, pAdapters ptr @8          (call once with pAdapters=NULL for the count)
D3DKMT_ENUMADAPTERS3      24  Filter u64 @0 (bit0 IncludeComputeOnly, bit1 IncludeDisplayOnly), NumAdapters u32 @8, pAdapters @16
D3DKMT_OPENADAPTERFROMLUID 12 AdapterLuid @0, hAdapter u32 @8
D3DKMT_CLOSEADAPTER        4  hAdapter u32 @0
D3DKMT_QUERYADAPTERINFO   24  hAdapter u32 @0, Type i32 @4, pPrivateDriverData ptr @8, PrivateDriverDataSize u32 @16

KMTQAITYPE (Type)            output (size)
  3  GETSEGMENTSIZE          D3DKMT_SEGMENTSIZEINFO (24): DedicatedVideoMemorySize u64 @0, DedicatedSystemMemorySize @8, SharedSystemMemorySize @16
  6  ADAPTERADDRESS          (12): BusNumber @0, DeviceNumber @4, FunctionNumber @8
  8  ADAPTERREGISTRYINFO     (2080): AdapterString WCHAR[260] @0, BiosString @520, DacType @1040, ChipType @1560
 15  ADAPTERTYPE             u32 bitfield (4)
 18  UMD_DRIVER_VERSION      i64 (8): four u16, high to low = a.b.c.d
 25  NODEMETADATA            D3DKMT_NODEMETADATA, #pragma pack(1), (78): NodeOrdinalAndAdapterIndex u32 @0 (in),
                             EngineType i32 @4, FriendlyName WCHAR[32] @8, Flags u32 @72, GpuMmuSupported u8 @76, IoMmuSupported u8 @77
 41  PHYSICALADAPTERPNPKEY   (24): PhysicalAdapterIndex u32 @0, PnPKeyType u32 @4 (1=HARDWARE, 2=SOFTWARE), pDest WCHAR* @8, pCchDest u32* @16
 60  QUERY_ADAPTER_UNIQUE_GUID (80): WCHAR[40] "{GUID}"
 62  ADAPTERPERFDATA         (64): PhysicalAdapterIndex u32 @0 (in), MemoryFrequency u64 @8, MaxMemoryFrequency @16, MaxMemoryFrequencyOC @24,
                             MemoryBandwidth @32, PCIEBandwidth @40, FanRPM u32 @48, Power u32 @52 (0.1 %), Temperature u32 @56 (0.1 °C), PowerStateOverride u8 @60
 63  ADAPTERPERFDATA_CAPS    (40): MaxFanRPM @24, TemperatureMax @28, TemperatureWarning @32
 65  DRIVER_DESCRIPTION      (8192): WCHAR[4096]
 70  WDDM_2_7_CAPS           u32 (4): bit0 HwSchSupported, bit1 HwSchEnabled

D3DKMT_QUERYSTATISTICS    0x328 (808), 8-byte aligned buffer, zeroed before each call
  Type i32 @0, AdapterLuid @4, hProcess HANDLE @16, QueryResult union @24 (0x308), query union u32 @0x320 (SegmentId / NodeId / PhysicalAdapterIndex)
  Type 0  ADAPTER          result: NbSegments u32 @+0, NodeCount u32 @+4, VidPnSourceCount u32 @+8
  Type 3  SEGMENT          result: CommitLimit u64 @+0, BytesCommitted @+8, BytesResident @+16, Aperture u32 @+40   (size 152)
  Type 5  NODE             result: GlobalInformation.RunningTime i64 @+0 (100 ns), ContextSwitch u32 @+8,
                                   SystemInformation.RunningTime @+272, NodePerfData @+544   (size 616)
  Type 6  PROCESS_NODE     result: RunningTime i64 @+0; needs hProcess with PROCESS_QUERY_INFORMATION
  Type 10 PHYSICAL_ADAPTER result: AdapterPerfData @+0 (Temperature @+56), AdapterPerfDataCaps @+64, GpuVersion @+104
  Types 11–19 (NODE2, SEGMENT2, …) are WDDM 3.1 / Windows 11 only.

PDH_RAW_COUNTER_ITEM_W     48  szName ptr @0, RawValue @8 { CStatus u32 @+0, TimeStamp FILETIME @+4, FirstValue i64 @+16, SecondValue @+24, MultiCount @+32 }
PDH_FMT_COUNTERVALUE_ITEM_W 24 szName ptr @0, FmtValue @8 { CStatus u32 @+0, doubleValue @+8 }
DXGI_ADAPTER_DESC1        312  VendorId @256, DedicatedVideoMemory @272, DedicatedSystemMemory @280, SharedSystemMemory @288, AdapterLuid @296, Flags @304
SP_DEVINFO_DATA 32, DEVPROPKEY 20
```

Segments on the RTX:

| Segment | Aperture | CommitLimit | Note |
|---|---|---|---|
| 0 | 1 | 68,345,618,432 | resident 0 |
| 1 | 0 | 25,503,465,472 | the VRAM segment |
| 2 | 1 | `UINT64_MAX` | the shared system-memory segment that holds resident bytes |

Never use CommitLimit as a total.

## Cost and boundedness

**D3DKMT path (memory, temperature, static values).**
- Per tick on this GPU: 3 `QueryStatistics` SEGMENT calls (NbSegments) and 1 `QueryAdapterInfo` ADAPTERPERFDATA call.
- Node statistics aren't needed if PDH supplies utilization. The 17-call tick in the test also included the 13 NODE calls.
- A hot call costs 0.3–0.7 µs. The first call after a 500 ms idle costs 10–700 µs of wall time (CPU and cache wake). At the 500 ms cadence the 17-call tick averaged 3.7 ms of wall time.
- Boundedness, all after `GC.Collect`:

  | Run | Private bytes | Handles |
  |---|---|---|
  | 120 ticks at 500 ms | +320 KB | +4 (runtime threads) |
  | 100,000 back-to-back ticks, measured every 20,000 | 10,856–11,028 KB, flat | 226–227 |
  | 2,000 × (`EnumAdapters3` + `CloseAdapter` of every handle) | +328 KB | ±0 |

- **Adapter handles.** `QueryStatistics` takes a LUID and needs no handle. `QueryAdapterInfo` needs one.
  - Keep the handle EnumAdapters3 returned for the RTX for the life of the process, and close the others at once. Or do what Task Manager does: `D3DKMTOpenAdapterFromLuid`, query, `D3DKMTCloseAdapter`.
  - D3DKMT handles are kernel-mode adapter handles, not Win32 handles. A leaked one would not show in `GetProcessHandleCount`, so close them by construction.
  - LUIDs change across driver restarts and reboots. On `STATUS_DEVICE_REMOVED` or `STATUS_INVALID_PARAMETER`, re-enumerate (**unverified**: not exercised).

**PDH `GPU Engine` under churn**: the spec's flatness proof.
- Setup:
  - Three in-process PDH consumers ran for 330 s, sampling every 500 ms: two `\GPU Engine(*)\Running Time` via `PdhGetRawCounterArrayW` (one logged, one silent), plus one `\GPU Engine(*)\Utilization Percentage` via `PdhGetFormattedCounterArrayW`.
  - Meanwhile a spawner started **896 short-lived processes**, 3 a second, each creating a real D3D11 device on the RTX, so each minted 13 new instances.
  - The consumers saw **7,982 (raw) / 7,631 (formatted) new instance names**. The live count was 494–520.
- Raw consumer:

  | Point | Private bytes | Δ vs t0 | Handles |
  |---|---|---|---|
  | t0 (tick 2) | 14,360 KB | | 308 |
  | t=30 s | 26,808 KB | +12.4 MB, warm-up | 315 |
  | t=60 s | 21,920 KB | +7.6 MB | 316 |
  | t=90 to t=300 s | 21,060–21,736 KB | | 309–313 |
  | t=329 s | 21,504 KB | +7.1 MB | 309 |

  From t=60 to t=329 (6,474 new instances) the slope is **−416 KB, i.e. none**. The managed heap stayed at 487–491 KB.
- Formatted consumer: same shape. 21,540 KB at t=60 and 21,860 KB at t=329: +320 KB over 6,201 new instances, inside the ±450 KB noise band seen between snapshots.
- **Control** (same consumer, 90 s, no churn, 0 new instances): t0 14,804 KB, t=30 s 26,960 KB, t=60 s 21,856 KB, t=89 s 22,168 KB. The same +12 MB spike and +7 MB plateau appeared, so the step is first-use warm-up (PDH, the perf provider, the runtime) and not caused by instance churn.
- **Result: flat after a one-time ~7 MB step on first use.** Over 5.5 minutes and ~8,000 instance births there was no measurable growth for either the raw or the formatted array.
- This was not a multi-day soak. The tmcs soak (`TMCS_DIAG`) should watch the GPU sampler's private bytes.
- Per tick: 0.64 ms (raw, including name parsing and string keys) and 0.93 ms (formatted).
- Rules for tmcs:
  - Keep one query and one counter for the process lifetime, and reuse the array buffer.
  - Rebuild the previous-sample map each tick from live names only.
  - Filter by LUID before allocating a key string.
  - Ignore items whose `CStatus` isn't `PDH_CSTATUS_VALID_DATA` / `NEW_DATA`.
  - Clamp negative per-instance deltas to 0. A PID reused within a tick, or a counter reset after a driver restart, gives a negative delta. The probe's PDH path lacked this guard.
  - Use `PdhAddEnglishCounterW`, not `PdhAddCounterW` (which Task Manager imports). Localized counter paths break on non-English Windows.
- Formatted mode needs two collects before it returns data.

**Recommended per-tick work for GPU 0:**
- 1 `PdhCollectQueryData` + 1 `PdhGetRawCounterArrayW`.
- 3 `D3DKMTQueryStatistics` SEGMENT calls.
- 1 `D3DKMTQueryAdapterInfo` ADAPTERPERFDATA call.

Everything else is read once at startup:
- ADAPTERTYPE, DRIVER_DESCRIPTION, GETSEGMENTSIZE, ADAPTERADDRESS, PNP keys, unique GUID and NODEMETADATA from D3DKMT.
- `qwMemorySize` and the DirectX cache from the registry.
- LocationInfo, DriverVersion and DriverDate from SetupAPI.

## Unverified / open

- How "GPU N" is numbered with more than one real GPU.
- Copy graph: max vs sum over the six Copy nodes. The recommendation is max.
- Temperature rounding (37.8 → 37 or 38).
- Driver date: locale short-date format vs a dash-to-slash rewrite of the registry string. Both give "7/22/2026" in en-US.
- Option (B) coverage when elevated: WUDFHost, dwm, csrss (PPL), pid 4.
- Global node statistics with HAGS off.
- Where Task Manager reads the physical VRAM size. `qwMemorySize` reproduces it exactly.
