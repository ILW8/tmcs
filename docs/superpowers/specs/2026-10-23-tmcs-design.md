# tmcs: design

Date: 2026-10-23
Owner: ILW8 (product owner; confirm every scope change with them)
Status: draft, awaiting owner approval

## 1. Intent

tmcs is the owner's own replacement for the Windows 10 Task Manager. The owner leaves Task Manager open for days on a busy machine (Win10 22H2 build 19045, Ryzen 9 7950X with 16 cores and 32 logical processors, 64 GB RAM, five disks, about a dozen network adapters, RTX 3090). They run many apps and many Claude Code sessions, so short-lived processes churn constantly.

The stock Task Manager leaks on that workload. Its update thread appends one icon per new process to the Details list view's image list on every tab, but it only removes icons while the Details tab is actually rendering. Left on any other tab, it leaks about 1,500 icons an hour (about 1 KB each), which reaches hundreds of MB over days. The next switch to Details then freezes for seconds while it removes the backlog. Patching Taskmgr.exe is not possible on this machine because locked code-integrity policy blocks a modified Microsoft binary. So the decision is a new, original program that the owner signs with their own certificate.

What success looks like: tmcs looks and behaves like the parts of Task Manager the owner uses. It can stay open for days on any tab without its memory or handle counts growing, and switching tabs never stalls.

## 2. Goals, non-goals, anti-goals

Goals
- A 1:1 visual and behavioral copy of the Windows 10 Task Manager, covering only the parts listed in section 4.
- Bounded memory and handle use, no matter how long it runs or how many processes come and go.
- Low CPU use while idle, and no sampling at all while paused.
- Runs elevated, so it can read every process.

Non-goals (for now)
- Every Task Manager feature. The owner uses a subset. Anything not in section 4 is out until the owner asks for it.
- A modern or redesigned look. This is a copy, not "inspired by".
- Dark mode. Windows 10's Task Manager is light-only.

Anti-goals (it would be a failure even if it technically works)
- Memory, GDI objects, USER objects or kernel handles that grow with process churn or with uptime.
- Any per-process growth in a structure that is only cleaned up when some view is visible. This is exactly the stock bug.
- A pause on switching tabs or restoring the window after a long time away.
- Features nobody asked for. The owner is deliberately escaping bloat.
- Carrying Microsoft's identity: no Microsoft version resource, assembly identity, product name, icons or other Microsoft resources embedded in the binary.

## 3. Decided with the owner

- Name: **tmcs** (assembly, exe, window title, settings folder).
- Stack: **C# WinForms on .NET 10** (current LTS, supported to Nov 2028), x64 only. Published as a **single-file, self-contained** exe so it is one clean file to sign.
- Elevation: manifest `requestedExecutionLevel level="requireAdministrator"`. See section 8 for development builds.
- Look: copy Windows 10 Task Manager 1:1 for each component built.
- Priority: the **Details tab** and the **Performance tab** are equal first priority. The **Processes tab** (grouped) comes later. **App history, Startup, Users and Services are out.**
- Repo: `git@github.com:ILW8/tmcs.git`, default branch `master`, work on `feat/tmcs-m1`, author `ILW8 <ilw8@41f.dev>`.

## 4. Scope for v1

### 4.1 Main window

- Title "tmcs" and an original app icon (not Task Manager's).
- Menu bar, copying Task Manager's structure but keeping only these items:
  - **File**: Run new task, Exit.
  - **Options**: Always on top.
  - **View**: Refresh now; Update speed ▸ High (0.5 s) / Normal (1 s) / Low (4 s) / Paused. These are Task Manager's four speeds.
- Out: Minimize on use, Hide when minimized, Set default tab, Show full account name, Show history for all processes, Group by type, Expand all, Collapse all, and "Fewer details".
- Tabs, in Task Manager's order, showing only the built ones: **Performance**, **Details**. The Processes tab will slot in first when it is built.
- Opens on the **Performance** tab, matching the owner's current Task Manager setting (`StartUpTab=1`).
- First-run window size matches the owner's Task Manager, about 1294×712. After that, position and size are remembered.
- Run new task: the same "Create new task" box Task Manager shows, which is the shell's Run dialog with the "Create this task with administrative privileges" checkbox. Unchecked, the task must start **non-elevated**, as it does in Task Manager.

Update speed and Paused
- One sampling clock drives everything. Paused stops the clock completely: no snapshots, no counter reads, no timers firing. Graphs and the list freeze in place.
- Refresh now (F5) takes exactly one sample, even while paused.
- Sampling continues while the window is minimized or on another tab, as Task Manager does, so graph history stays continuous. Work that only feeds a view that isn't showing may be skipped. But nothing may pile up for a view that isn't showing; see section 5.

### 4.2 Details tab

Columns. The owner's current Task Manager layout, decoded from their registry settings, is the default. Widths are at 100% scale and get scaled for DPI.

| # | Column | Default width | Shown by default |
|---|---|---|---|
| 1 | Name (with icon) | 312 | yes |
| 2 | PID | 49 | yes |
| 3 | Status | 100 | yes |
| 4 | User name | 80 | yes |
| 5 | CPU | 40 | yes |
| 6 | Memory (active private working set) | 108 | yes |
| 7 | Command line | 1033 | yes |
| 8 | UAC virtualization | 100 | yes |
| 9 | Threads | Task Manager's default | no, available in the picker |
| 10 | Handles | Task Manager's default | no, available in the picker |
| 11 | Commit size | Task Manager's default | no, available in the picker |

- Only these 11 columns exist. Task Manager has 48; the rest are out.
- Values are formatted exactly as Task Manager formats them, for example memory as `12,345 K`, CPU as `00`, Status as Running / Suspended / Not responding, and UAC virtualization as Disabled / Enabled / Not allowed. Where in doubt, match the stock Task Manager side by side.
- The list includes the "System Idle Process" row, as Task Manager does.

Header
- Click to sort, with Task Manager's sort arrow. Drag to reorder. Drag edges to resize.
- Right-click the header for **Hide column** / **Select columns**.
- **Select columns** is Task Manager's checkbox dialog, listing only the 11 columns above.
- Visible columns, order, widths and sort are remembered between runs.

Rows
- Native list control in virtual mode, plus an icon (see 5.2).
- The selection follows the process, not the row index, across refreshes and re-sorts. Scroll position stays stable.
- Typing jumps to a matching name, like the stock list.
- Delete ends the task after confirmation.
- An **End task** button sits at the bottom right, as in Task Manager.

Right-click a process. This is Task Manager's "CPU Context" menu, trimmed and with one addition:
- **End task**, **End process tree**: each with Task Manager's confirmation dialog, plus its error dialog when access is denied.
- **Set priority ▸** Realtime / High / Above normal / Normal / Below normal / Low, with Task Manager's confirmation.
- **Set affinity**: Task Manager's "Processor affinity" dialog, with \<All Processors\> and CPU 0 to CPU 31 checkboxes.
- **Open file location**: opens Explorer with the file selected.
- **Properties**: the shell's file Properties dialog.
- **Copy ▸ PID / Image path**: an addition the owner asked for; it isn't in Task Manager.
- Out: Provide feedback, Analyze wait chain, Debug, UAC virtualization, Create dump file, Search online, Go to service(s).
- Items that don't apply are disabled, for example Open file location for processes with no image path such as System, Registry or Memory Compression.

### 4.3 Performance tab

Sidebar, as in Task Manager: an entry with a live mini graph and summary text for each device.
- **CPU**, **Memory**, **Disk 0–4** (labelled with drive letters), **every network adapter**, and **GPU 0** (RTX 3090).
- List only real GPUs, as Task Manager does. The DisplayLink and Parsec virtual adapters don't appear.
- **New, beyond Task Manager**:
  - Right-click a network adapter → **Hide**. It's remembered.
  - Right-click anywhere in the sidebar → **Show hidden adapters (N)** toggles all hidden adapters back into view and out again.
  - The owner will hide the nine Hyper-V vEthernet adapters and Wi-Fi 2, leaving NC550SFP Port 2 (their internet path) and Tailscale.
  - Hiding applies to network adapters only.
- Out: Task Manager's sidebar menu (Summary view, Show/Hide graphs, Copy).

Right-hand pane. Graphs plus the numbers below them, 1:1 with Task Manager. Graphs cover 60 seconds of history in fixed-size buffers.
- **CPU**:
  - **One graph per logical processor**: 32 graphs in Task Manager's grid layout. This is fixed, not switchable.
  - Numbers: Utilization, Speed, Processes, Threads, Handles, Up time, Base speed, Sockets, Cores, Logical processors, Virtualization, and L1/L2/L3 cache.
- **Memory**:
  - Memory usage graph and the Memory composition bar.
  - Numbers: In use (Compressed), Available, Committed, Cached, Paged pool, Non-paged pool, Speed, Slots used, Form factor, Hardware reserved.
- **Disk N**:
  - Active time graph and Disk transfer rate graph (read/write).
  - Numbers: Active time, Average response time, Read speed, Write speed, Capacity, Formatted, System disk, Page file, Type.
- **Network adapter**:
  - Throughput graph (send/receive) with an auto scale.
  - Numbers: Send, Receive, Adapter name, DNS name, Connection type, IPv4 address, IPv6 address. For Wi-Fi: SSID, Signal strength.
- **GPU 0**:
  - Four engine graphs (3D, Copy, Video Encode, Video Decode), Dedicated GPU memory usage graph, Shared GPU memory usage graph.
  - Numbers: Utilization, Dedicated GPU memory, Shared GPU memory, GPU Memory, Driver version, Driver date, DirectX version, Physical location, Hardware reserved memory.
- Out: the pane's right-click menu (Change graph to, Show kernel times, Graph summary view, View, View network details, Copy), and the "Open Resource Monitor" link.

## 5. Engineering constraints (non-negotiable)

### 5.1 Bounded state
- Every per-process structure is keyed by **process identity = (PID, creation time)**, never PID alone, because PIDs get reused. This covers the process table, cached user names, command lines, icon references and parent links.
- Entries are removed when the process exits, on the same tick, whatever tab is showing.
- No growth path may depend on a view being visible to clean up.
- Caches keyed by something other than a process must be bounded or reclaimed. Examples are the SID → user name cache and the icon store. When nothing alive references an entry, drop it, optionally after a short grace period.
- Reuse sampling buffers across ticks. For example, the `NtQuerySystemInformation` buffer grows only when it is too small, and never shrinks back and forth.

### 5.2 Icons
This is the critical design.
- Never add one image per process.
- Icons are deduplicated by **executable path**: identical processes share one icon. The store's size tracks the number of **distinct live executable paths** and shrinks when paths stop running. Slots for dead paths are reused (replaced in place), not appended forever.
- The builder may use the shared system image list (`SHGetFileInfo` with `SHGFI_SYSICONINDEX`, or `SHGetImageList(SHIL_SMALL)`, attached with `LVS_SHAREIMAGELISTS`) if they can show it stays bounded. A slot-reusing private image list keyed by path also meets the requirement.
- Every `HICON` obtained is destroyed. Processes with no image path get Task Manager's generic executable icon equivalent, which must be an original or a shell-provided icon, not a Microsoft resource copied into tmcs.

### 5.3 Virtualized UI
- The Details list uses WinForms `ListView` with `VirtualMode = true`. Rows are produced on demand for visible items only.
- No per-row controls, handles or GDI objects.
- Graphs are owner-drawn into double-buffered controls. Pens, brushes and fonts are created once and reused, and the GDI object count must stay flat.

### 5.4 Elevation-dependent data
- Per-process details that need a process handle are read once per process identity and cached until it exits. That covers command line, user name and UAC virtualization state.
- Live values such as CPU time, memory, threads and handles come from the system-wide snapshot each tick.
- Processes that can't be opened, such as protected ones, show blanks rather than errors, as Task Manager does.

### 5.5 Responsiveness
- Switching tabs, restoring the window or un-pausing after any amount of time must be instant (under about 100 ms). The view rebuilds from the current snapshot; there is never a backlog to drain.

## 6. Data sources

This is guidance; the builder chooses.
- **Process snapshot**: `NtQuerySystemInformation(SystemProcessInformation)` once per tick. It gives name, PID, parent PID, session, creation time, kernel and user time, private working set (`WorkingSetPrivateSize`), commit (`PagefileUsage`/private page count), thread count, handle count, and thread states, which reveal suspended processes.
- **CPU column**: per-process (kernel + user) time delta ÷ (elapsed time × logical processor count). System Idle Process takes the idle share.
- **Command line**: `NtQueryInformationProcess(ProcessCommandLineInformation)` with `PROCESS_QUERY_LIMITED_INFORMATION`.
- **User name**: `OpenProcessToken` → `TokenUser` → `LookupAccountSid`, cached per SID. Task Manager shows the short account name (SYSTEM, LOCAL SERVICE, DWM-1, the owner's user name).
- **UAC virtualization**: `TokenVirtualizationAllowed` and `TokenVirtualizationEnabled`.
- **Status "Not responding"**: top-level windows checked with `IsHungAppWindow`, only while the Details tab is showing.
- **Per-logical-processor CPU**: `SystemProcessorPerformanceInformation` or PDH `Processor Information`. Task Manager is believed to use frequency-scaled "% Processor Utility" on the Performance tab but processor time in Details. Verify which one each view uses, and match the stock Task Manager side by side.
- **Memory**: `GetPerformanceInfo`, `GlobalMemoryStatusEx`, `NtQuerySystemInformation` memory-list info for the composition bar. Speed, slots and form factor come from SMBIOS/WMI, read once at startup.
- **Disk**: `IOCTL_DISK_PERFORMANCE` per physical drive, or PDH PhysicalDisk. Static info comes from `IOCTL_STORAGE_QUERY_PROPERTY` and volume mapping.
- **Network**: `GetIfTable2`/`GetIfEntry2` octet counters, plus `GetAdaptersAddresses` for names and IPs.
- **GPU**: `D3DKMTQueryStatistics` (node running time, segment usage), or PDH `GPU Engine` / `GPU Adapter Memory`. If PDH wildcard counters are used, prove their memory stays flat under churn, because `GPU Engine` instances are per process.

## 7. Reference material for the 1:1 copy

The owner does not allow computer use (screen or mouse control). Get Task Manager's look and behavior without touching the screen:

- **Owner's layout**: `HKCU\Software\Microsoft\Windows\CurrentVersion\TaskManager\Preferences`, already decoded into section 4.2. Decoding notes: `side-session-notes\local_1ff79418-…\taskmgr-user-layout.md`.
- **Menus**: menu resources in `C:\Windows\System32\en-US\Taskmgr.exe.mui`, already dumped:
  - 30200: Details row menu
  - 30201: header menu
  - 30205: main menu
  - 30206 / 30207: Performance pane and sidebar menus
- **Dialogs**: dialog resources in the same MUI, for example 34701 "Processor affinity" (202×178 DLU) and 30130 "Select processors".
- **Strings**: the MUI string table. Details column names are at 32003 onward and 37007. The column table sits in Taskmgr.exe `.rdata` at RVA 0xf5f20 (48 × 0x38-byte entries holding flags, numeric flag, default width and string ID).
- **Layout, colors and spacing**: `UIFILE` resources 30001 and 30024 in Taskmgr.exe. These are compiled DirectUI markup (`duib`) and would need decoding.
- **Fallbacks for visuals and values**:
  - Ask the owner for screenshots, which they take when convenient.
  - With the owner's OK at the time, run one elevated helper (one UAC prompt) that reads Task Manager through UI Automation and captures its window with `PrintWindow`. It must never move the mouse, send keystrokes or take focus.
  - UI Automation also exposes the Details grid's cell text, which lets the builder compare tmcs's numbers against Task Manager's.
- These are references only. Recreate the layout, text and behavior. Don't embed Microsoft binaries, icons, bitmaps or markup in tmcs.

## 8. Build, packaging, signing

- `net10.0-windows`, `UseWindowsForms`, `RuntimeIdentifier win-x64`, `SelfContained`, `PublishSingleFile`.
- Per-monitor DPI awareness (PerMonitorV2).
- Assembly and version resource identify tmcs and the owner, not Microsoft.
- Single instance: launching tmcs again brings the running window forward, as Task Manager does.
- Settings: `%APPDATA%\tmcs\settings.json`, written when they change (debounced) and on exit.
- Signing: the owner signs the published exe with their own certificate (`signtool sign …`). The repo documents the command but stores no keys.
- Development builds: every elevated launch shows a UAC prompt, which disrupts the owner. So Debug builds use an `asInvoker` manifest (less data, no prompt), and Release/published builds use `requireAdministrator`. Full-privilege tests run on the published build with the owner's OK at the time.
- A one-time scheduled task "run with highest privileges" would allow prompt-free elevated launches. It is a persistent system change, so only with the owner's explicit permission.

## 9. Acceptance tests

These run at the end of every milestone, not just the first.

1. **Churn soak**: generate heavy short-lived process churn, for example several parallel loops of `cmd /c ping -n 2 127.0.0.1` (hundreds of processes a minute), for at least 10 minutes per state, with tmcs in each state:
   - Details tab
   - Performance tab: CPU pane, Network pane, GPU pane
   - Minimized
   - Paused

   Record tmcs's private bytes, kernel handle count, GDI objects and USER objects (`GetGuiResources`) every 10 s into a CSV.
   - **Pass**: after a 2-minute warm-up, private bytes show no upward trend beyond noise; GDI and USER counts stay flat (within ±5); kernel handles stay bounded.
   - The test script lives in the repo (for example `tools/soak.ps1`) and runs without elevation where possible. Querying an elevated process with `PROCESS_QUERY_LIMITED_INFORMATION` works from a non-elevated script.
2. **No backlog stall**: after 30 minutes of churn on the Performance tab, switching to Details takes under about 100 ms.
3. **True pause**: tmcs's CPU time doesn't advance meaningfully over 5 paused minutes, and no sampling calls happen.
4. **Idle cost**: at Normal speed, tmcs's CPU use on each tab is no higher than the stock Task Manager's on the same tab, measured on this machine.
5. **Icon dedup**: run 50 copies of the same exe and confirm one icon slot. Let them exit and confirm the slot is reclaimed. A diagnostics switch such as `--diag`, which logs internal counts like icon slots and cache sizes to a file, is acceptable as a test aid. It isn't a user feature.
6. **Fidelity**: the owner's side-by-side visual check against Task Manager for each built component.

## 10. Later (not in v1; each needs the owner's go-ahead)

- **Processes tab**, grouped (Apps / Background processes / Windows processes, heat-map columns).
- **Live filter box** for the Details list.
- **CPU topology view**: show which logical processors belong to which CCD (the 7950X has 2) and which logical processor is another's SMT sibling. Likely source: `GetLogicalProcessorInformationEx` (`RelationProcessorCore` for SMT siblings; `RelationCache` L3 groups for CCDs).

## 11. Left to the builder

- Project and solution layout (suggested: `src/Tmcs`, `tests/Tmcs.Tests`, `tools/`, `docs/`).
- Threading model: suggested is sampling on a background thread with UI-thread rendering.
- Milestone order inside v1. The Details tab, Performance tab and actions all ship in v1, and the acceptance tests run after each milestone.
- Exact data APIs, within section 6's constraints.
- GC configuration (for example workstation, non-concurrent) if it helps keep memory low.

## 12. Calls made without the owner's explicit say-so (please check)

- The Select columns dialog lists only the 11 supported columns, not all 48.
- Copy is a submenu with **PID** and **Image path**.
- GPU pane: the four engine graphs are fixed to 3D / Copy / Video Encode / Video Decode. Task Manager's per-graph engine dropdowns are left out.
- The CPU graph is fixed to per-logical-processor; the "Change graph to" menu is out.
- The pane's right-click menu, the sidebar's stock right-click menu and the "Open Resource Monitor" link are out.
- No system tray icon. Task Manager shows a CPU meter in the notification area while it runs.
- tmcs always opens on Performance, rather than remembering the last tab.
- First-run defaults come from the owner's decoded Task Manager layout.
- Settings live in `%APPDATA%\tmcs\settings.json`.
- Single instance.
- Debug builds run `asInvoker` to avoid UAC prompts during development.
