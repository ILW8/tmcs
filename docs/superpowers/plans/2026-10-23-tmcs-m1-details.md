# tmcs Milestone 1 (Details tab core) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a leak-free tmcs window whose Details tab lists every process with deduplicated icons, the owner's columns, sorting, column picker, update speed / pause, and saved settings. Then prove with an unattended churn soak that memory and handle counts stay flat.

**Architecture:**
- One WinForms UI thread does all the work. A `SampleClock` tick reads one `NtQuerySystemInformation` snapshot and diffs it into a `ProcessTable`, keyed by (PID, creation time).
- Per-process static info is queried once. Exited processes leave the table, and release their icon, on the same tick no matter which view is showing.
- The Details list is a virtual-mode `ListView` subclass. It shares one slot-reusing native image list owned by a path-keyed `IconStore`.
- Menus are native Win32 menus, so they match Task Manager.
- A `--diag`/`--scenario` test mode lets tmcs switch its own view state and log its own memory and handle counts, so the soak needs no screen automation.

**Tech Stack:** C# 14 / .NET 10 (SDK 10.0.204), WinForms, `LibraryImport` P/Invoke (ntdll, kernel32, advapi32, user32, shell32, comctl32, uxtheme), System.Text.Json, xUnit 2.9.3, PowerShell 7 for tools.

**Spec:** `docs/superpowers/specs/2026-10-23-tmcs-design.md` (approved). This is milestone 1 of v1. Milestone 2 (Performance tab) and milestone 3 (Details actions: right-click menu, End task, Run new task) get their own plans after this lands.

## Global Constraints

- Target `net10.0-windows`, `UseWindowsForms`, `PlatformTarget x64`, `RuntimeIdentifier win-x64`, `Nullable enable`, `AllowUnsafeBlocks true`.
- Names: assembly and exe `tmcs`, root namespace `Tmcs`, window title `tmcs`, settings at `%APPDATA%\tmcs\settings.json`.
- Version resource: Product `tmcs`, Company `ILW8`, Copyright `© 2026 ILW8`, Version `0.1.0`. No Microsoft identity, product name, icons, bitmaps or markup in the binary.
- Manifests: Release `requireAdministrator`; Debug `asInvoker` (no UAC prompt during development). DPI via `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>`, not in the manifest.
- Per-process state is keyed by `ProcessIdentity(Pid, CreateTime)`, never PID alone. Exits are removed on the same tick on every view. No cleanup may depend on a view being visible.
- Icons are deduplicated by image path (case-insensitive). Slots are reused, never appended per process. Every `HICON` obtained is destroyed.
- The Details list uses `ListView.VirtualMode = true`. No per-row controls or GDI objects. Pens, brushes and fonts are created once.
- Update speeds: High 500 ms, Normal 1000 ms, Low 4000 ms, Paused (timer disabled, zero samples). Refresh now (F5) takes exactly one sample, even while paused.
- Details columns, titles verbatim and default widths at 96 DPI. The first eight are visible by default, in this order:

  | Column | Width |
  |---|---|
  | `Name` | 312 |
  | `PID` | 49 |
  | `Status` | 100 |
  | `User name` | 80 |
  | `CPU` | 40 |
  | `Memory (active private working set)` | 108 |
  | `Command line` | 1033 |
  | `UAC virtualization` | 100 |
  | `Threads` | 34 |
  | `Handles` | 34 |
  | `Commit size` | 80 |

  `Threads`, `Handles` and `Commit size` are hidden by default.
- First-run window size 1294×712. Default sort: Name, ascending.
- **Owner rules:**
  - Never use computer use, screenshots or UI automation of the owner's desktop.
  - Never trigger a UAC prompt, change system settings, or `git push` without the owner's OK in chat at the time.
  - Never add a feature the spec doesn't list.
  - Tests that need a window use the off-screen helper from Task 1: off-screen, no activation, no taskbar or Alt-Tab entry.
- Commits: the repo is already configured with author `ILW8 <ilw8@41f.dev>` on branch `feat/tmcs-m1`. End every commit message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Processes tmcs can't open** must show blanks (or `SYSTEM` for PIDs 0 and 4), never throw, never leak a handle. This covers protected/PPL processes, ones that exit between the snapshot and the query, Registry, Memory Compression and Secure System. Tests: Task 4 `Query_mismatched_identity_returns_blanks`, `Query_does_not_leak_handles`, `Query_system_and_idle_report_SYSTEM`.
2. **PID reuse between ticks** must produce a new row with fresh user, command line and icon, not stale data. Tests: Task 3 `Pid_reuse_with_new_create_time_is_a_new_row`; Task 4 identity check.
3. **Selection during heavy churn.** The selected process stays selected when rows re-sort or are inserted above it. If it exits, the selection clears and doesn't jump to a neighbor. The scroll position doesn't jump. Tests: Task 6 `Restore_follows_identity_after_resort`, `Exited_selection_is_dropped`; Task 9 `Item_count_change_keeps_top_index`.
4. **Very long command lines** (Claude Code and Node.js lines run to thousands of characters) render without error or stall. Tests: Task 9 `Very_long_text_is_served_without_error`.
5. **A bad or stale settings file** falls back to defaults or clamps to the visible work area. That includes a corrupt file, a saved position on a monitor that's gone, and unknown column IDs. Tests: Task 7 `Corrupt_file_yields_defaults`, `Normalize_repairs_column_list`, `Clamp_moves_offscreen_window_onto_a_screen`.

---

### Task 1: Solution skeleton, manifests, icon, test harness

**Files:**
- Create: `tmcs.slnx`, `.gitignore`, `README.md`
- Create: `src/Tmcs/Tmcs.csproj`, `src/Tmcs/app.manifest`, `src/Tmcs/app.debug.manifest`, `src/Tmcs/Program.cs`, `src/Tmcs/MainForm.cs`, `src/Tmcs/tmcs.ico`
- Create: `tools/make-icon.ps1`
- Create: `tests/Tmcs.Tests/Tmcs.Tests.csproj`, `tests/Tmcs.Tests/Support/Sta.cs`, `tests/Tmcs.Tests/Support/OffscreenForm.cs`, `tests/Tmcs.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: `Tmcs.MainForm : Form` (empty for now, title `tmcs`). Test helpers:
  - `Sta.Run(Action body)`: runs on a new STA thread and rethrows exceptions.
  - `OffscreenForm : Form`: `ShowWithoutActivation => true`, `StartPosition Manual`, `Location (-32000,-32000)`, `ShowInTaskbar false`, `FormBorderStyle None`, `WS_EX_TOOLWINDOW` in `CreateParams`; `ShowOffscreen()` calls `Show()` and then `Application.DoEvents()`.

- [ ] **Step 1: Scaffold.** Run `dotnet new sln -n tmcs`, `dotnet new winforms -n Tmcs -o src/Tmcs`, `dotnet new xunit -n Tmcs.Tests -o tests/Tmcs.Tests`, `dotnet sln add src/Tmcs tests/Tmcs.Tests` and `dotnet add tests/Tmcs.Tests reference src/Tmcs`. Delete the template `Form1*` files.
- [ ] **Step 2: Edit `src/Tmcs/Tmcs.csproj`:**
  - Properties: `AssemblyName tmcs`, `RootNamespace Tmcs`, `OutputType WinExe`, the Global Constraints framework/platform/nullable/unsafe/DPI properties, the version-resource properties, `ApplicationIcon tmcs.ico` and `ConcurrentGarbageCollection false`.
  - Manifests: `ApplicationManifest app.manifest`, plus `ApplicationManifest app.debug.manifest` when `'$(Configuration)'=='Debug'`.
  - Item: `<InternalsVisibleTo Include="Tmcs.Tests" />`.
  - `Tmcs.Tests.csproj`: `TargetFramework net10.0-windows`, `UseWindowsForms true`, `PlatformTarget x64`, `AllowUnsafeBlocks true`.
- [ ] **Step 3: Write the manifests.** Each has `trustInfo` with the right `requestedExecutionLevel` (`requireAdministrator` / `asInvoker`, `uiAccess="false"`) and the Windows 10 `supportedOS` GUID `{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}`. No `dpiAware` elements.
- [ ] **Step 4: Write `tools/make-icon.ps1` and generate the icon.** The script draws an original glyph with System.Drawing: a rounded dark-slate square with a rising green line graph. It renders sizes 16, 24, 32, 48 and 256 and writes `src/Tmcs/tmcs.ico` (ICO header, directory entries, PNG-encoded images). Run `pwsh -File tools/make-icon.ps1`. Expect `src/Tmcs/tmcs.ico` to exist and be more than 1 KB.
- [ ] **Step 5: Program and window.**
  - `Program.Main` (`[STAThread]`): `ApplicationConfiguration.Initialize(); Application.Run(new MainForm());`.
  - `MainForm`: `Text = "tmcs"`, `ClientSize` 1294×712, `Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!)`.
- [ ] **Step 6: Write the smoke test.**

```csharp
public class SmokeTests
{
    [Fact]
    public void MainForm_creates_offscreen_with_title_tmcs() => Sta.Run(() =>
    {
        using var f = new MainForm();
        f.CreateControl();
        Assert.Equal("tmcs", f.Text);
    });
}
```
- [ ] **Step 7: Run the tests.** Run `dotnet test`. Expect `Passed! - Failed: 0, Passed: 1`.
- [ ] **Step 8: `.gitignore` and `README.md`.** `.gitignore` covers `bin/ obj/ artifacts/ .vs/ .idea/ *.user TestResults/`. `README.md` gets a one-paragraph purpose, `dotnet build` / `dotnet test`, and a pointer to the spec.
- [ ] **Step 9: Commit.** Run `git add -A`, then commit with message `feat: tmcs solution skeleton, manifests, icon, test harness`.

---

### Task 2: Process snapshot reader

**Files:**
- Create: `src/Tmcs/Native/NtDll.cs`, `src/Tmcs/Processes/RawProcess.cs`, `src/Tmcs/Processes/ProcessSnapshotParser.cs`, `src/Tmcs/Processes/SystemProcessReader.cs`
- Test: `tests/Tmcs.Tests/Processes/ProcessSnapshotParserTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct RawProcess(int Pid, int ParentPid, long CreateTime, string Name, long KernelTime, long UserTime, long PrivateWorkingSet, long CommitSize, int Threads, int Handles, int SessionId, int BasePriority, bool IsSuspended)`
  - `interface IProcessSnapshotSource { void Read(List<RawProcess> into); }`
  - `static class ProcessSnapshotParser { static void Parse(ReadOnlySpan<byte> buffer, nint baseAddress, List<RawProcess> into); }`. It clears `into`. `baseAddress` is the address of `buffer[0]` and is used to resolve `ImageName.Buffer` pointers.
  - `sealed class SystemProcessReader : IProcessSnapshotSource`. It keeps one `byte[]`, starting at 512 KB, and doubles it on `STATUS_INFO_LENGTH_MISMATCH (0xC0000004)`. It never shrinks.
  - `NtDll.NtQuerySystemInformation(int infoClass, void* buffer, int length, out int returnLength)`, with `SystemProcessInformation = 5`.
- x64 layout, as facts the parser needs. Process entry, 0x100 bytes, followed by `NumberOfThreads` × 0x50-byte thread entries:

  | Offset | Field |
  |---|---|
  | 0x00 | `NextEntryOffset` u32 |
  | 0x04 | `NumberOfThreads` u32 |
  | 0x08 | `WorkingSetPrivateSize` i64 |
  | 0x20 | `CreateTime` i64 |
  | 0x28 | `UserTime` i64 |
  | 0x30 | `KernelTime` i64 |
  | 0x38 | `ImageName.Length` u16 (bytes) |
  | 0x40 | `ImageName.Buffer` ptr |
  | 0x48 | `BasePriority` i32 |
  | 0x50 | `UniqueProcessId` ptr |
  | 0x58 | `InheritedFromUniqueProcessId` ptr |
  | 0x60 | `HandleCount` u32 |
  | 0x64 | `SessionId` u32 |
  | 0xB8 | `PagefileUsage` (commit size) ptr-size |

  Thread entry: 0x44 `ThreadState` u32 (Waiting = 5), 0x48 `WaitReason` u32 (Suspended = 5).
- Derived fields:
  - `IsSuspended` = `Threads > 0` and every thread is `ThreadState==5 && WaitReason==5`.
  - PID 0 with an empty name gets the name `System Idle Process`.

- [ ] **Step 1: Write the failing parser test.**

```csharp
[Fact]
public unsafe void Parse_reads_entries_names_counters_and_suspension()
{
    var buf = new byte[4096];
    fixed (byte* p = buf)
    {
        nint b = (nint)p;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x00), 0x200);           // idle → next
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(0x30), 5_000_000);         // idle kernel time
        int e = 0x200, nameOff = e + 0x100 + 2 * 0x50;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(e + 0x04), 2);
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x08), 40_960);
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x20), 133_000_000_000_000_000);
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x28), 20_000);
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x30), 30_000);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(e + 0x38), 14);           // "cmd.exe" bytes
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x40), b + nameOff);
        Encoding.Unicode.GetBytes("cmd.exe").CopyTo(buf, nameOff);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(e + 0x48), 8);
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x50), 1234);
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x58), 99);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(e + 0x60), 77);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(e + 0x64), 1);
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0xB8), 24_576);
        for (int t = 0; t < 2; t++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(e + 0x100 + t * 0x50 + 0x44), 5);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(e + 0x100 + t * 0x50 + 0x48), 5);
        }
        var list = new List<RawProcess>();
        ProcessSnapshotParser.Parse(buf, b, list);

        Assert.Equal(2, list.Count);
        Assert.Equal(new RawProcess(0, 0, 0, "System Idle Process", 5_000_000, 0, 0, 0, 0, 0, 0, 0, false), list[0]);
        Assert.Equal(new RawProcess(1234, 99, 133_000_000_000_000_000, "cmd.exe", 30_000, 20_000, 40_960, 24_576, 2, 77, 1, 8, true), list[1]);
    }
}

[Fact]
public void Live_snapshot_contains_System_and_this_process()
{
    var list = new List<RawProcess>();
    new SystemProcessReader().Read(list);
    Assert.Contains(list, r => r.Pid == 4 && r.Name == "System");
    var me = list.Single(r => r.Pid == Environment.ProcessId);
    long expected = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToFileTimeUtc();
    Assert.InRange(me.CreateTime, expected - 10_000_000, expected + 10_000_000); // within 1 s
}
```
- [ ] **Step 2: Run the tests to confirm they fail.** Run `dotnet test --filter ProcessSnapshotParserTests`. Expect FAIL: types not defined.
- [ ] **Step 3: Implement** `NtDll`, `RawProcess`, `ProcessSnapshotParser.Parse` (walk `NextEntryOffset` until 0) and `SystemProcessReader.Read` (pin the buffer with `fixed`, call the API, retry with a bigger buffer, then parse).
- [ ] **Step 4: Run the tests.** Run `dotnet test --filter ProcessSnapshotParserTests`. Expect PASS (2 tests).
- [ ] **Step 5: Commit** with message `feat: NtQuerySystemInformation process snapshot reader`.

---

### Task 3: Process table (identity, diff, CPU %)

**Files:**
- Create: `src/Tmcs/Processes/ProcessIdentity.cs`, `ProcessRow.cs`, `ProcessStaticInfo.cs`, `ProcessStatus.cs`, `IProcessInfoSource.cs`, `CpuMath.cs`, `ProcessTable.cs`
- Test: `tests/Tmcs.Tests/Processes/ProcessTableTests.cs`

**Interfaces:**
- Consumes: `RawProcess` (Task 2).
- Produces:
  - `readonly record struct ProcessIdentity(int Pid, long CreateTime)`
  - `enum ProcessStatus { Running, Suspended, NotResponding }`
  - `enum UacVirtualization { Unknown, NotAllowed, Disabled, Enabled }`
  - `sealed record ProcessStaticInfo(string? ImagePath, string? CommandLine, string? UserName, UacVirtualization Uac)`, with `static ProcessStaticInfo Blank`
  - `interface IProcessInfoSource { ProcessStaticInfo Query(ProcessIdentity id); }`
  - `static class CpuMath { static double Percent(long deltaCpu100ns, long deltaWall100ns, int logicalProcessors); }`: clamped to [0, 100], and 0 when `deltaWall100ns <= 0`.
  - `sealed class ProcessRow`:
    - Get-only: `Id`, `Name`, `ParentPid`, `Info` (set once).
    - Settable: `IconSlot`, `Status`, `CpuPercent`, `PrivateWorkingSet`, `CommitSize`, `Threads`, `Handles`, `KernelTime`, `UserTime`.
  - `sealed class ProcessTable`:
    - Constructor: `ProcessTable(IProcessInfoSource info, int logicalProcessors)`.
    - Members: `IReadOnlyList<ProcessRow> Rows`, `int Count`, `event Action<ProcessRow>? Added`, `event Action<ProcessRow>? Removed`.
    - `void Apply(List<RawProcess> snapshot, long timestamp100ns)`: adds new identities (one `Query` each), updates live fields and CPU %, sets `Status` to `Suspended` or `Running`, and removes missing identities. `Removed` is raised before `Added` within a tick.
    - `void ApplyHung(IReadOnlySet<int> hungPids)`: sets `NotResponding` on hung, non-suspended rows.

- [ ] **Step 1: Write the failing tests.** Use a `FakeInfo : IProcessInfoSource` that counts `Query` calls per identity and returns `new ProcessStaticInfo("C:\\x\\" + pid, "cmd " + pid, "u", UacVirtualization.Disabled)`. Helper: `static RawProcess P(int pid, long ct, long k = 0, long u = 0, bool susp = false) => new(pid, 1, ct, "p" + pid, k, u, 100, 200, 3, 4, 1, 8, susp);`.

```csharp
[Fact] public void Apply_adds_rows_and_queries_static_info_once()
{ var info = new FakeInfo(); var t = new ProcessTable(info, 4); var added = 0; t.Added += _ => added++;
  t.Apply([P(10, 1), P(11, 1)], 0); t.Apply([P(10, 1), P(11, 1)], 10_000_000);
  Assert.Equal(2, t.Count); Assert.Equal(2, added); Assert.Equal(1, info.Calls[new(10, 1)]); }

[Fact] public void Exited_process_is_removed_on_the_same_tick()
{ var t = new ProcessTable(new FakeInfo(), 4); ProcessRow? gone = null; t.Removed += r => gone = r;
  t.Apply([P(10, 1), P(11, 1)], 0); t.Apply([P(10, 1)], 10_000_000);
  Assert.Equal(1, t.Count); Assert.Equal(new ProcessIdentity(11, 1), gone!.Id); }

[Fact] public void Pid_reuse_with_new_create_time_is_a_new_row()
{ var info = new FakeInfo(); var t = new ProcessTable(info, 4); var events = new List<string>();
  t.Removed += r => events.Add("-" + r.Id.CreateTime); t.Added += r => events.Add("+" + r.Id.CreateTime);
  t.Apply([P(10, 1)], 0); t.Apply([P(10, 2)], 10_000_000);
  Assert.Equal(["+1", "-1", "+2"], events); Assert.Equal(new ProcessIdentity(10, 2), t.Rows.Single().Id);
  Assert.Equal(1, info.Calls[new(10, 2)]); }

[Fact] public void Cpu_percent_is_time_delta_over_wall_times_logical_processors()
{ var t = new ProcessTable(new FakeInfo(), 4);
  t.Apply([P(10, 1, k: 0, u: 0)], 0); t.Apply([P(10, 1, k: 2_000_000, u: 2_000_000)], 10_000_000);
  Assert.Equal(10.0, t.Rows.Single().CpuPercent, 6); }

[Fact] public void New_process_shows_zero_cpu_on_first_tick()
{ var t = new ProcessTable(new FakeInfo(), 4); t.Apply([P(10, 1, k: 9_000_000)], 0); Assert.Equal(0, t.Rows.Single().CpuPercent); }

[Fact] public void Hung_marks_not_responding_but_suspended_wins()
{ var t = new ProcessTable(new FakeInfo(), 4); t.Apply([P(10, 1), P(11, 1, susp: true)], 0);
  t.ApplyHung(new HashSet<int> { 10, 11 });
  Assert.Equal(ProcessStatus.NotResponding, t.Rows.Single(r => r.Id.Pid == 10).Status);
  Assert.Equal(ProcessStatus.Suspended, t.Rows.Single(r => r.Id.Pid == 11).Status); }
```
- [ ] **Step 2: Run the tests to confirm they fail.** Run `dotnet test --filter ProcessTableTests`. Expect FAIL: types not defined.
- [ ] **Step 3: Implement.** `ProcessTable` keeps `Dictionary<ProcessIdentity, ProcessRow>` plus a `List<ProcessRow>` for `Rows`. It uses a reusable `HashSet<ProcessIdentity>` per tick to find exits. It removes with swap-remove (O(1); order doesn't matter, since the view sorts) and stores the last timestamp for the wall delta. Don't allocate per tick beyond new rows.
- [ ] **Step 4: Run the tests.** Run `dotnet test --filter ProcessTableTests`. Expect PASS (6 tests).
- [ ] **Step 5: Commit** with message `feat: process table keyed by pid+create time with cpu deltas`.

---

### Task 4: Real per-process info, SID cache, hung-window scan

**Files:**
- Create: `src/Tmcs/Native/Kernel32.cs`, `Advapi32.cs`, `User32.cs`
- Create: `src/Tmcs/Processes/SidNameCache.cs`, `ProcessInfoSource.cs`, `HungWindowScanner.cs`
- Test: `tests/Tmcs.Tests/Processes/ProcessInfoSourceTests.cs`, `SidNameCacheTests.cs`

**Interfaces:**
- Consumes: `IProcessInfoSource`, `ProcessIdentity`, `ProcessStaticInfo`, `UacVirtualization` (Task 3); `SystemProcessReader` (Task 2, used in tests).
- Produces:
  - `sealed class SidNameCache`:
    - Constructor: `SidNameCache(Func<byte[], string?> lookup, int capacity = 256)`.
    - `string? Resolve(byte[] sid)`: keyed by the SID bytes (Base64 string key). Clears itself before inserting when `Count == capacity`.
    - `int Count`.
    - `static string? LookupAccountName(byte[] sid)`: `LookupAccountSidW`, short name only.
  - `sealed class ProcessInfoSource : IProcessInfoSource`:
    - Constructor: `ProcessInfoSource(SidNameCache sids)`.
    - `static bool TryEnableDebugPrivilege()`.
    - `ProcessStaticInfo Query(ProcessIdentity id)` behavior:
      - PID 0 → `(null, null, "SYSTEM", NotAllowed)`; PID 4 → `(null, null, "SYSTEM", NotAllowed)`.
      - Otherwise: `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION=0x1000)`, then verify `GetProcessTimes` creation == `id.CreateTime`, else `Blank`.
      - Path: `QueryFullProcessImageNameW`.
      - Command line: `NtQueryInformationProcess(ProcessCommandLineInformation=60)`.
      - Token: `OpenProcessToken(TOKEN_QUERY)` for `TokenUser=1`, `TokenVirtualizationAllowed=23` and `TokenVirtualizationEnabled=24`. Not allowed → `NotAllowed`; allowed and not enabled → `Disabled`; enabled → `Enabled`; token unavailable → `Unknown`.
      - Every handle is closed in `finally`.
  - `sealed class HungWindowScanner { void Scan(HashSet<int> hungPids); }`: clears, then `EnumWindows` over visible top-level windows; `IsHungAppWindow` → `GetWindowThreadProcessId`. The callback delegate is a cached static field.

- [ ] **Step 1: Write the failing tests.**

```csharp
static ProcessIdentity Me()
{ var l = new List<RawProcess>(); new SystemProcessReader().Read(l); var r = l.Single(x => x.Pid == Environment.ProcessId); return new(r.Pid, r.CreateTime); }
static ProcessInfoSource Src() => new(new SidNameCache(SidNameCache.LookupAccountName));

[Fact] public void Query_current_process_returns_path_command_line_user()
{ var i = Src().Query(Me());
  Assert.Equal(Environment.ProcessPath, i.ImagePath, ignoreCase: true);
  Assert.Contains(Path.GetFileName(Environment.ProcessPath!), i.CommandLine!, StringComparison.OrdinalIgnoreCase);
  Assert.Equal(Environment.UserName, i.UserName, ignoreCase: true);
  Assert.NotEqual(UacVirtualization.Unknown, i.Uac); }

[Fact] public void Query_system_and_idle_report_SYSTEM()
{ var s = Src(); Assert.Equal("SYSTEM", s.Query(new(4, 0)).UserName); Assert.Equal("SYSTEM", s.Query(new(0, 0)).UserName); Assert.Null(s.Query(new(4, 0)).ImagePath); }

[Fact] public void Query_mismatched_identity_returns_blanks()
{ var me = Me(); Assert.Equal(ProcessStaticInfo.Blank, Src().Query(me with { CreateTime = me.CreateTime + 1 }));
  Assert.Equal(ProcessStaticInfo.Blank, Src().Query(new(0x7FFFFFF0, 1))); }

[Fact] public void Query_does_not_leak_handles()
{ var l = new List<RawProcess>(); new SystemProcessReader().Read(l); var s = Src();
  var before = Process.GetCurrentProcess().HandleCount;
  for (int n = 0; n < 5; n++) foreach (var r in l) s.Query(new(r.Pid, r.CreateTime));
  Process.GetCurrentProcess().Refresh(); Assert.InRange(Process.GetCurrentProcess().HandleCount, 0, before + 20); }
```

  `SidNameCacheTests`:
  - `Resolves_once_per_sid`: two `Resolve` calls with equal byte arrays → the lookup is invoked once.
  - `Clears_when_capacity_reached`: capacity 2, three distinct SIDs → `Count == 1` after the third.
  - `HungWindowScanner_scan_excludes_current_process`: after `Scan`, the set doesn't contain `Environment.ProcessId`.
- [ ] **Step 2: Run the tests to confirm they fail.** Run `dotnet test --filter "ProcessInfoSourceTests|SidNameCacheTests"`. Expect FAIL.
- [ ] **Step 3: Implement** the P/Invoke declarations (`LibraryImport`, `SetLastError = true`, UTF-16 strings) and the three classes.
- [ ] **Step 4: Run the tests.** Run the same filter. Expect PASS. The current-process UAC value is `Disabled` or `NotAllowed` depending on elevation; both pass.
- [ ] **Step 5: Commit** with message `feat: per-process static info, bounded sid cache, hung window scan`.

---

### Task 5: Icon store and native image list

**Files:**
- Create: `src/Tmcs/Native/Shell32.cs`, `ComCtl32.cs`
- Create: `src/Tmcs/Icons/IIconLoader.cs`, `IImageListSink.cs`, `IconStore.cs`, `NativeImageList.cs`, `ShellIconLoader.cs`
- Test: `tests/Tmcs.Tests/Icons/IconStoreTests.cs`, `NativeIconTests.cs`

**Interfaces:**
- Produces:
  - `interface IIconLoader { nint LoadSmallIcon(string path); void DestroyIcon(nint hIcon); }`: returns 0 on failure.
  - `interface IImageListSink { int Count { get; } int Add(nint hIcon); void Replace(int index, nint hIcon); }`
  - `sealed class IconStore`:
    - `const int GenericSlot = 0`.
    - Constructor: `IconStore(IImageListSink sink, IIconLoader loader, nint genericIcon, int releasedPoolCapacity = 64)`. It adds the generic icon at slot 0.
    - `int Acquire(string? imagePath)` / `void Release(string? imagePath)`.
    - Counters: `int LiveCount` (distinct paths with refcount > 0), `int PooledCount`, `int SlotCount` (= `sink.Count`).
  - `sealed class NativeImageList : IImageListSink, IDisposable`:
    - Constructor: `NativeImageList(int iconSize)`, which calls `ImageList_Create(size, size, ILC_COLOR32 0x20 | ILC_MASK 0x1, 64, 32)`.
    - Members: `nint Handle`. `Add` → `ImageList_ReplaceIcon(h, -1, icon)`; `Replace` → `ImageList_ReplaceIcon(h, i, icon)`; `Dispose` → `ImageList_Destroy`.
  - `sealed class ShellIconLoader : IIconLoader`:
    - `LoadSmallIcon` → `SHGetFileInfoW(path, 0, ref info, size, SHGFI_ICON 0x100 | SHGFI_SMALLICON 0x1)`.
    - `static nint LoadGenericIcon()` → `SHGetStockIconInfo(SIID_APPLICATION 2, SHGSI_ICON 0x100 | SHGSI_SMALLICON 0x1)`.
- Algorithm (decided):
  - Entries: `Dictionary<string, Entry{Slot, RefCount}>(StringComparer.OrdinalIgnoreCase)`, plus an LRU `LinkedList<string>` of refcount-0 entries, plus a `Stack<int>` of free slots.
  - **Acquire, known path:** if it's pooled, unpool it; then `RefCount++`.
  - **Acquire, new path:** load the icon. On failure, create the entry with `Slot = GenericSlot`. On success, pop a free slot and `Replace`, or else `Add`. Then `DestroyIcon` the loaded handle.
  - **Release:** `RefCount--`. At 0, push the entry onto the front of the LRU. If `PooledCount > capacity`, evict the oldest: push its slot onto the free stack (unless it's `GenericSlot`) and remove the entry.
  - Null or empty path → `GenericSlot`, with no entry.

- [ ] **Step 1: Write the failing tests.** Fakes: `FakeSink` counts `Add`/`Replace`. `FakeLoader` returns incrementing handles, or 0 for paths starting with `"bad:"`, and counts loads and destroys.

```csharp
[Fact] public void Same_path_shares_one_slot()
{ var (s, sink, ld) = Make(); var slots = Enumerable.Range(0, 50).Select(_ => s.Acquire(@"C:\w\cmd.exe")).Distinct().ToList();
  Assert.Single(slots); Assert.Equal(2, s.SlotCount); Assert.Equal(1, s.LiveCount); Assert.Equal(1, ld.Loads); }

[Fact] public void Path_compare_is_case_insensitive()
{ var (s, _, ld) = Make(); Assert.Equal(s.Acquire(@"C:\W\CMD.EXE"), s.Acquire(@"c:\w\cmd.exe")); Assert.Equal(1, ld.Loads); }

[Fact] public void Released_path_is_pooled_and_reacquired_without_reload()
{ var (s, _, ld) = Make(); var a = s.Acquire("p"); s.Release("p"); Assert.Equal(0, s.LiveCount); Assert.Equal(1, s.PooledCount);
  Assert.Equal(a, s.Acquire("p")); Assert.Equal(1, ld.Loads); }

[Fact] public void Churn_of_distinct_paths_stays_bounded_and_destroys_every_icon()
{ var (s, sink, ld) = Make(capacity: 4);
  for (int i = 0; i < 1000; i++) { s.Acquire("p" + i); s.Release("p" + i); }
  Assert.InRange(s.SlotCount, 1, 1 + 4 + 1); Assert.Equal(ld.Loads, ld.Destroys); Assert.Equal(4, s.PooledCount); }

[Fact] public void Load_failure_returns_generic_and_consumes_no_slot()
{ var (s, sink, _) = Make(); Assert.Equal(IconStore.GenericSlot, s.Acquire("bad:x")); Assert.Equal(1, s.SlotCount); }

[Fact] public void Null_path_returns_generic() { var (s, _, _) = Make(); Assert.Equal(IconStore.GenericSlot, s.Acquire(null)); s.Release(null); }
```

  `NativeIconTests` (STA):
  - `Shell_loader_loads_cmd_icon_and_returns_zero_for_missing_file`: `cmd.exe` from `Environment.SystemDirectory` gives a nonzero handle; `C:\nope\missing.exe` gives 0 within 500 ms.
  - `Image_list_count_stays_flat_under_real_path_churn`: an `IconStore` over a `NativeImageList` with capacity 8. Loop 20 rounds over 30 exe paths from `Environment.SystemDirectory`, acquiring and releasing. Assert `ImageList_GetImageCount(handle) == store.SlotCount` and `≤ 1 + 8 + 1`. Assert the process GDI count (`GetGuiResources(GetCurrentProcess(), 0)`) after the loop is ≤ the count before + 5.
- [ ] **Step 2: Run the tests to confirm they fail.** Run `dotnet test --filter "IconStoreTests|NativeIconTests"`. Expect FAIL.
- [ ] **Step 3: Implement** the classes above.
- [ ] **Step 4: Run the tests.** Same filter. Expect PASS.
- [ ] **Step 5: Commit** with message `feat: path-keyed icon store with slot reuse over a native image list`.

---

### Task 6: Details column model, formatting, sorting, search, selection mapping

**Files:**
- Create: `src/Tmcs/Details/DetailsColumn.cs`, `ColumnDef.cs`, `DetailsColumns.cs`, `ValueFormat.cs`, `RowSorter.cs`, `RowSearch.cs`, `SelectionMap.cs`
- Test: `tests/Tmcs.Tests/Details/DetailsModelTests.cs`

**Interfaces:**
- Consumes: `ProcessRow`, `ProcessStatus`, `UacVirtualization`, `ProcessIdentity` (Task 3).
- Produces:
  - `enum DetailsColumn { Name, Pid, Status, UserName, Cpu, ActivePrivateWorkingSet, CommandLine, UacVirtualization, Threads, Handles, CommitSize }`
  - `sealed record ColumnDef(DetailsColumn Id, string Title, int DefaultWidth, bool DefaultVisible, HorizontalAlignment Align, Func<ProcessRow, string> Text, Comparison<ProcessRow> Compare)`
  - `static class DetailsColumns { static IReadOnlyList<ColumnDef> All { get; } static ColumnDef Get(DetailsColumn id); }`: in enum order, with the Global Constraints titles and widths. Right-aligned: Pid, Cpu, ActivePrivateWorkingSet, Threads, Handles, CommitSize.
  - `static class ValueFormat`:
    - `string Kilobytes(long bytes)` → `(bytes / 1024).ToString("N0") + " K"`.
    - `string Cpu(double pct)` → `((int)Math.Round(pct, MidpointRounding.AwayFromZero)).ToString("00")`.
    - `string Count(int n)` → `n.ToString("N0")`.
    - `string Status(ProcessStatus s)` → `Running` / `Suspended` / `Not responding`.
    - `string Uac(UacVirtualization u)` → `""` / `Not allowed` / `Disabled` / `Enabled`.
  - `static class RowSorter { static void Sort(List<ProcessRow> rows, DetailsColumn column, bool descending); }`: ties go to Name (ascending, current culture, case-insensitive), then PID (ascending), whatever the direction.
  - `static class RowSearch { static int FindByPrefix(IReadOnlyList<ProcessRow> rows, string prefix, int startIndex); }`: case-insensitive on Name, wraps around, returns −1 if not found.
  - `static class SelectionMap`:
    - `static void Capture(IReadOnlyList<ProcessRow> rows, IEnumerable<int> indices, HashSet<ProcessIdentity> into)`
    - `static List<int> Restore(IReadOnlyList<ProcessRow> rows, IReadOnlySet<ProcessIdentity> selected)`: ascending indices of rows still present.

- [ ] **Step 1: Write the failing tests.** Set `CultureInfo.CurrentCulture = new("en-US")` in the test class constructor.

```csharp
[Fact] public void Columns_match_owner_layout()
{ Assert.Equal(["Name","PID","Status","User name","CPU","Memory (active private working set)","Command line","UAC virtualization","Threads","Handles","Commit size"], DetailsColumns.All.Select(c => c.Title));
  Assert.Equal([312,49,100,80,40,108,1033,100,34,34,80], DetailsColumns.All.Select(c => c.DefaultWidth));
  Assert.Equal(8, DetailsColumns.All.Count(c => c.DefaultVisible)); Assert.False(DetailsColumns.Get(DetailsColumn.Threads).DefaultVisible); }

[Theory] [InlineData(12_641_280, "12,345 K")] [InlineData(0, "0 K")] [InlineData(1023, "0 K")]
public void Kilobytes_format(long b, string s) => Assert.Equal(s, ValueFormat.Kilobytes(b));

[Theory] [InlineData(0.4, "00")] [InlineData(0.5, "01")] [InlineData(9.6, "10")] [InlineData(100, "100")]
public void Cpu_format(double p, string s) => Assert.Equal(s, ValueFormat.Cpu(p));

[Fact] public void Status_and_uac_strings()
{ Assert.Equal("Not responding", ValueFormat.Status(ProcessStatus.NotResponding)); Assert.Equal("Not allowed", ValueFormat.Uac(UacVirtualization.NotAllowed)); Assert.Equal("", ValueFormat.Uac(UacVirtualization.Unknown)); }

[Fact] public void Sort_by_cpu_descending_breaks_ties_by_name_then_pid()
{ var rows = new List<ProcessRow> { R(3, "b", cpu: 5), R(2, "a", cpu: 5), R(1, "a", cpu: 5), R(4, "z", cpu: 9) };
  RowSorter.Sort(rows, DetailsColumn.Cpu, descending: true); Assert.Equal([4, 1, 2, 3], rows.Select(r => r.Id.Pid)); }

[Fact] public void FindByPrefix_is_case_insensitive_and_wraps()
{ var rows = new[] { R(1, "chrome.exe"), R(2, "Code.exe"), R(3, "cmd.exe") };
  Assert.Equal(1, RowSearch.FindByPrefix(rows, "co", 0)); Assert.Equal(0, RowSearch.FindByPrefix(rows, "chr", 2)); Assert.Equal(-1, RowSearch.FindByPrefix(rows, "zz", 0)); }

[Fact] public void Restore_follows_identity_after_resort()
{ var rows = new List<ProcessRow> { R(1, "a"), R(2, "b"), R(3, "c") }; var sel = new HashSet<ProcessIdentity>();
  SelectionMap.Capture(rows, [1], sel); rows.Reverse(); Assert.Equal([1], SelectionMap.Restore(rows, sel)); rows.Insert(0, R(9, "x")); Assert.Equal([2], SelectionMap.Restore(rows, sel)); }

[Fact] public void Exited_selection_is_dropped()
{ var rows = new List<ProcessRow> { R(1, "a"), R(2, "b") }; var sel = new HashSet<ProcessIdentity>();
  SelectionMap.Capture(rows, [1], sel); rows.RemoveAt(1); Assert.Empty(SelectionMap.Restore(rows, sel)); }
```

  `R(pid, name, cpu)` builds a `ProcessRow` with `ProcessIdentity(pid, 1)`. Add a test-visible constructor or factory `ProcessRow.ForTest(...)` in Task 3's type if needed.
- [ ] **Step 2: Run the tests to confirm they fail.** Run `dotnet test --filter DetailsModelTests`. Expect FAIL.
- [ ] **Step 3: Implement** the types above. Each column's `Compare` compares the underlying value (numbers numerically, text with `StringComparer.CurrentCultureIgnoreCase`), not the formatted string.
- [ ] **Step 4: Run the tests.** Same filter. Expect PASS.
- [ ] **Step 5: Commit** with message `feat: details column model, formatting, sorting, search, selection map`.

---

### Task 7: Settings (defaults, JSON, repair, window clamp)

**Files:**
- Create: `src/Tmcs/Settings/AppSettings.cs`, `ColumnSetting.cs`, `SettingsStore.cs`, `WindowPlacement.cs`
- Create: `src/Tmcs/Sampling/UpdateSpeed.cs`
- Test: `tests/Tmcs.Tests/Settings/SettingsTests.cs`

**Interfaces:**
- Consumes: `DetailsColumn`, `DetailsColumns` (Task 6).
- Produces:
  - `enum UpdateSpeed { High, Normal, Low, Paused }`
  - `sealed class ColumnSetting { DetailsColumn Id; bool Visible; int Width; int DisplayIndex; }`: get/set properties, `Width` in 96-DPI units.
  - `sealed class AppSettings`:
    - Window: `int? WindowX, WindowY`; `int WindowWidth = 1294, WindowHeight = 712`; `bool Maximized`.
    - `bool AlwaysOnTop`; `UpdateSpeed UpdateSpeed = Normal`.
    - Details: `List<ColumnSetting> DetailsColumns`, `DetailsColumn SortColumn = Name`, `bool SortDescending`.
    - `List<string> HiddenAdapters`: unused until M2.
    - `static AppSettings CreateDefault()`: columns from `DetailsColumns.All` defaults, display index = enum order.
    - `void Normalize()`: drops unknown or duplicate IDs, appends missing columns with their defaults, forces Name visible, clamps widths to ≥ 20, and renumbers display indices densely (visible first, in current display order).
    - `static IReadOnlyList<DetailsColumn> VisibleInDisplayOrder(AppSettings s)`.
  - `sealed class SettingsStore`:
    - Constructor: `SettingsStore(string path)`; `static string DefaultPath` = `%APPDATA%\tmcs\settings.json`.
    - `AppSettings Load()`: defaults on a missing or corrupt file, and always `Normalize()`.
    - `void Save(AppSettings s)`: indented JSON with enums as strings, written to `path + ".tmp"`, then `File.Move(overwrite: true)`.
  - `static class WindowPlacement { static Rectangle Clamp(Rectangle saved, IReadOnlyList<Rectangle> workingAreas); }`: if the saved rectangle overlaps no working area by at least 50×50 px, centre it on `workingAreas[0]`, shrinking to fit.

- [ ] **Step 1: Write the failing tests.**
  - `Default_matches_owner_layout`: visible-in-order = the first eight columns; `WindowWidth == 1294 && WindowHeight == 712`; `UpdateSpeed == Normal`; `SortColumn == Name && !SortDescending`.
  - `Round_trips_through_json`: change speed to Low, hide `CommandLine`, set Threads visible with width 50, save to a temp path, load, assert equal values, assert no `.tmp` file remains.
  - `Corrupt_file_yields_defaults`: write `"{not json"`, `Load()` → equals `CreateDefault()` visible order.
  - `Normalize_repairs_column_list`: input holds `[Pid, Pid(dup), Name(Visible=false, Width=3)]` plus an unknown enum value `(DetailsColumn)99` → 11 columns, Name visible with width 20, no duplicates.
  - `Clamp_moves_offscreen_window_onto_a_screen`: `Clamp(new(5000, 5000, 1294, 712), [new(0, 0, 1920, 1040)])` → contained in `(0,0,1920,1040)`. An on-screen rectangle is returned unchanged.
- [ ] **Step 2: Run the tests to confirm they fail.** Run `dotnet test --filter SettingsTests`. Expect FAIL.
- [ ] **Step 3: Implement** with System.Text.Json (`JsonStringEnumConverter`, `WriteIndented`). Catch `JsonException`, `IOException` and `UnauthorizedAccessException` in `Load` and fall back to defaults.
- [ ] **Step 4: Run the tests.** Same filter. Expect PASS.
- [ ] **Step 5: Commit** with message `feat: settings with owner-layout defaults, json store, repair, window clamp`.

---

### Task 8: Sample clock (update speed, pause, refresh now)

**Files:**
- Create: `src/Tmcs/Sampling/ISampleTimer.cs`, `WinFormsSampleTimer.cs`, `SampleClock.cs`
- Test: `tests/Tmcs.Tests/Sampling/SampleClockTests.cs`

**Interfaces:**
- Consumes: `UpdateSpeed` (Task 7).
- Produces:
  - `interface ISampleTimer { int IntervalMs { get; set; } bool Enabled { get; set; } event EventHandler? Tick; }`
  - `sealed class WinFormsSampleTimer : ISampleTimer, IDisposable`: wraps `System.Windows.Forms.Timer`.
  - `sealed class SampleClock`:
    - Constructor: `SampleClock(ISampleTimer timer)`.
    - `UpdateSpeed Speed { get; set; }`: sets interval and enabled.
    - `event Action? Sample`; `void RefreshNow()`; `long SamplesTaken`.
    - `static int IntervalFor(UpdateSpeed s)`: High 500, Normal 1000, Low 4000; throws for Paused.

- [ ] **Step 1: Write the failing tests** with a `FakeTimer : ISampleTimer` exposing `Fire()`:
  - `Intervals_match_task_manager`: 500, 1000 and 4000 for High, Normal and Low.
  - `Speed_sets_timer_interval_and_enables_it`: set Low → `IntervalMs == 4000 && Enabled`.
  - `Paused_disables_timer_and_takes_no_samples`: set Paused → `!Enabled`; `Fire()` → `SamplesTaken == 0`.
  - `RefreshNow_samples_once_even_when_paused`: Paused, `RefreshNow()` → `SamplesTaken == 1` and `Sample` raised once.
- [ ] **Step 2: Run the tests to confirm they fail.** Run `dotnet test --filter SampleClockTests`. Expect FAIL.
- [ ] **Step 3: Implement.** `Tick` raises `Sample` only when `Speed != Paused` (belt and braces).
- [ ] **Step 4: Run the tests.** Expect PASS.
- [ ] **Step 5: Commit** with message `feat: sample clock with task manager speeds and true pause`.

---

### Task 9: ProcessListView control

**Files:**
- Create: `src/Tmcs/Native/UxTheme.cs`; extend `User32.cs` (`SendMessage` overloads, `TrackPopupMenuEx`, header messages)
- Create: `src/Tmcs/Details/ProcessListView.cs`
- Test: `tests/Tmcs.Tests/Details/ProcessListViewTests.cs`

**Interfaces:**
- Consumes: `NativeImageList.Handle` (Task 5).
- Produces: `sealed class ProcessListView : ListView`.
  - `nint SharedImageList { get; set; }`: sent with `LVM_SETIMAGELIST (0x1003, LVSIL_SMALL 1)` in `OnHandleCreated` and whenever it's set.
  - `CreateParams` adds `LVS_SHAREIMAGELISTS (0x40)`, so the control never destroys the list.
  - The constructor sets `View = Details`, `VirtualMode = true`, `FullRowSelect = true`, `HideSelection = false`, `AllowColumnReorder = true`, `DoubleBuffered = true`, `HeaderStyle = Clickable`. `OnHandleCreated` calls `SetWindowTheme(Handle, "Explorer", null)`.
  - `void SetSortArrow(int columnIndex, bool descending)`: `HDM_GETITEMW 0x120B` / `HDM_SETITEMW 0x120C` with `HDF_SORTUP 0x400` / `HDF_SORTDOWN 0x200`. Clears the flags on every other column. −1 clears all.
  - `event Action<int, Point>? HeaderContextMenu`: column index under the cursor (via `HDM_HITTEST`), −1 when none, plus the screen point. Raised from `WndProc` on `WM_CONTEXTMENU (0x7B)` when `wParam` is the header handle (`LVM_GETHEADER 0x101F`).
  - `void SetItemCountKeepScroll(int count)`: sets `VirtualListSize` and keeps `LVM_GETTOPINDEX (0x1027)` unchanged. If WinForms resets it, restore it by scrolling (`LVM_SCROLL`) by the row-height delta.

- [ ] **Step 1: Write the failing tests** (STA, `OffscreenForm`, 5 columns, items from a `RetrieveVirtualItem` handler):
  - `Virtual_item_image_index_reaches_native_control`: items use `ImageIndex = 2` over a 3-icon native list; `LVM_GETITEMW` with `LVIF_IMAGE` on item 1 → `iImage == 2`, and again after recreating the handle (reflection `Control.RecreateHandle`). This repeats the spike that passed on 2026-10-23.
  - `Sort_arrow_sets_header_format_flags`: `SetSortArrow(4, true)` → header item 4 `fmt` has `0x200` and no `0x400`; item 0 has neither.
  - `Item_count_change_keeps_top_index`: 500 items; shown off-screen at 600 px height; `EnsureVisible(150)`; record top index; `SetItemCountKeepScroll(520)` → same top index.
  - `Very_long_text_is_served_without_error`: subitem 1 is a 40,000-char string; `LVM_GETITEMTEXTW` for (row 0, subitem 1) with a 1024-char buffer → returns the string's prefix with no exception.
  - `Header_right_click_raises_HeaderContextMenu_with_column`: get the header item 2 rectangle (`HDM_GETITEMRECT 0x1207`), convert its centre to screen, send `WM_CONTEXTMENU(header, point)` → the event fires with column index 2.
- [ ] **Step 2: Run the tests to confirm they fail.** Run `dotnet test --filter ProcessListViewTests`. Expect FAIL.
- [ ] **Step 3: Implement `ProcessListView`.**
- [ ] **Step 4: Run the tests.** Expect PASS (5 tests).
- [ ] **Step 5: Commit** with message `feat: virtual process list view with shared image list, sort arrow, header menu hook`.

---

### Task 10: DetailsView and Select columns dialog

**Files:**
- Create: `src/Tmcs/Details/DetailsView.cs`, `src/Tmcs/Details/SelectColumnsDialog.cs`, `src/Tmcs/Ui/NativeMenu.cs`
- Test: `tests/Tmcs.Tests/Details/DetailsViewTests.cs`

**Interfaces:**
- Consumes: `ProcessTable`, `ProcessRow` (Task 3); `DetailsColumns`, `ValueFormat`, `RowSorter`, `RowSearch`, `SelectionMap` (Task 6); `AppSettings`, `ColumnSetting` (Task 7); `ProcessListView` (Task 9); `NativeImageList` (Task 5).
- Produces:
  - `sealed class NativeMenu : IDisposable`:
    - `static NativeMenu Popup(params (string text, int id)[] items)`.
    - `int Track(Control owner, Point screen)`: `TrackPopupMenuEx` with `TPM_RETURNCMD`; returns the id or 0.
    - The same builder makes the main menu in Task 11 (`AddPopup(string text, NativeMenu child)`, `AddItem`, `AddSeparator`, `nint Handle`).
  - `sealed class DetailsView : UserControl`:
    - Constructor: `DetailsView(ProcessTable table, NativeImageList images, AppSettings settings)`. It hosts a docked `ProcessListView`.
    - `void RefreshRows()`: copies `table.Rows` into its own `List<ProcessRow>`, sorts per settings, restores the selection by identity, calls `SetItemCountKeepScroll`, then `Invalidate`.
    - `void SortBy(DetailsColumn c)`: a new column sorts ascending; the same column toggles direction. Updates settings and the arrow.
    - `void HideColumn(DetailsColumn c)`: Name can't be hidden.
    - `void ApplyColumnSettings()`: rebuilds `Columns` from `AppSettings.VisibleInDisplayOrder`, with widths scaled by `DeviceDpi / 96`.
    - `event EventHandler? SettingsChanged`: raised on sort, hide, select, reorder and width change, with widths saved back in 96-DPI units.
    - `IReadOnlyList<ProcessRow> DisplayedRows`.
    - Header right-click shows a `NativeMenu` with `Hide column` (disabled on Name or when no column is hit) and `Select columns`.
    - `SearchForVirtualItem` uses `RowSearch.FindByPrefix`. `RetrieveVirtualItem` fills text via `ColumnDef.Text` and `ImageIndex = row.IconSlot`.
  - `sealed class SelectColumnsDialog : Form`:
    - Constructor: `SelectColumnsDialog(AppSettings settings)`. A `CheckedListBox` of all 11 titles in `DetailsColumns.All` order, with Name checked and locked; OK and Cancel buttons.
    - `IReadOnlySet<DetailsColumn> Selected`. On OK, the caller applies `Selected` to the visibility flags and calls `Normalize()`.

- [ ] **Step 1: Write the failing tests** (STA, `OffscreenForm`, a `ProcessTable` fed by a `FakeInfo` with three rows, en-US culture):
  - `Shows_visible_columns_in_settings_order`: header texts equal the eight default titles in order.
  - `Rows_render_formatted_values`: after `RefreshRows()` with Name ascending, row 0 column 0 text (`LVM_GETITEMTEXTW`) is the alphabetically first name, and the memory column reads `"12,345 K"` for a row with `PrivateWorkingSet = 12_641_280`.
  - `SortBy_same_column_toggles_direction`: `SortBy(Cpu)` twice → `settings.SortDescending == true` after the second call, and `DisplayedRows` are in descending CPU order.
  - `HideColumn_updates_settings_and_header`: `HideColumn(UacVirtualization)` → 7 headers, `settings.DetailsColumns.Single(c => c.Id == UacVirtualization).Visible == false`, `SettingsChanged` raised. `HideColumn(Name)` → no change.
  - `Selection_follows_process_across_refresh`: select the row with PID 2, change the CPU values so the order flips, `RefreshRows()` → `SelectedIndices` points at the row whose `Id.Pid == 2`.
  - `Select_columns_dialog_lists_11_columns_with_name_locked`: construct the dialog → 11 items, item 0 is `"Name"` and checked, and `Selected` contains the 8 defaults.
- [ ] **Step 2: Run the tests to confirm they fail.** Run `dotnet test --filter DetailsViewTests`. Expect FAIL.
- [ ] **Step 3: Implement** `NativeMenu`, `DetailsView` and `SelectColumnsDialog`. Wire `ColumnClick` → `SortBy`, `ColumnReordered` / `ColumnWidthChanged` → settings, and `HeaderContextMenu` → the menu, with the dialog shown via `ShowDialog(this)`.
- [ ] **Step 4: Run the tests.** Expect PASS (6 tests).
- [ ] **Step 5: Commit** with message `feat: details view with sorting, column hide/select, selection tracking`.

---

### Task 11: MainForm wiring, menus, single instance, launch options

**Files:**
- Modify: `src/Tmcs/MainForm.cs`, `src/Tmcs/Program.cs`
- Create: `src/Tmcs/AppServices.cs`, `src/Tmcs/LaunchOptions.cs`, `src/Tmcs/Ui/CommandIds.cs`
- Test: `tests/Tmcs.Tests/MainFormTests.cs`, `tests/Tmcs.Tests/LaunchOptionsTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `sealed record LaunchOptions(string? DiagPath, string? Scenario)`, with `static LaunchOptions Parse(string[] args)` for `--diag <path>` and `--scenario <spec>`. Unknown arguments are ignored. `bool IsTestMode => DiagPath != null`.
  - `sealed record AppServices(IProcessSnapshotSource Snapshots, IProcessInfoSource Info, IIconLoader Icons, nint GenericIcon, ISampleTimer Timer, SettingsStore Store, LaunchOptions Options)`, with `static AppServices CreateReal(LaunchOptions o)`.
  - `MainForm(AppServices services)`:
    - Owns the `SampleClock`, `ProcessTable`, `IconStore`, `NativeImageList` (icon size from `GetSystemMetricsForDpi(SM_CXSMICON 49, DeviceDpi)`), `HungWindowScanner` and `DetailsView`.
    - A `TabControl` with one tab, `Details`. The Performance tab arrives in M2.
    - The native main menu is set with `SetMenu`:
      - `&File` → `E&xit`
      - `&Options` → `&Always on top`
      - `&View` → `&Refresh now`, `&Update speed` ▸ `&High`, `&Normal`, `&Low`, `&Paused`
    - `WM_COMMAND` dispatches by `CommandIds`. `WM_INITMENUPOPUP` sets the speed radio check and the Always on top check. F5 → `RefreshNow` via `ProcessCmdKey`.
    - On each sample:
      1. `Snapshots.Read`
      2. `table.Apply(list, Stopwatch-based 100 ns timestamp)`
      3. only if the Details tab is selected and the form isn't minimized: hung scan, then `table.ApplyHung`, then `detailsView.RefreshRows()`
    - Becoming visible (tab selected, window restored) calls `RefreshRows()` at once.
    - `table.Added` → `row.IconSlot = icons.Acquire(row.Info.ImagePath)`; `table.Removed` → `icons.Release(...)`. These always run, whatever the view.
    - Settings: `SettingsChanged` and window move/resize start a 2-second one-shot save timer, and `FormClosing` saves at once. Startup applies `WindowPlacement.Clamp`, `AlwaysOnTop` → `TopMost`, and `UpdateSpeed`. The first sample is taken immediately.
    - Exposes `internal` test hooks: `ProcessTable Table`, `IconStore Icons`, `SampleClock Clock`, `DetailsView Details`.
  - `Program`:
    - Not in test mode: a named mutex `Local\tmcs.single-instance`. If it's already held, find the other `tmcs` process's main window, `ShowWindow(SW_RESTORE)` + `SetForegroundWindow`, and exit.
    - Elevated: `ProcessInfoSource.TryEnableDebugPrivilege()`.
    - `Application.Run(new MainForm(AppServices.CreateReal(opts)))`.

- [ ] **Step 1: Write the failing tests.**
  - `LaunchOptionsTests`:
    - `Parses_diag_and_scenario`: `["--diag", "c:\\t\\d.csv", "--scenario", "details:1"]` → both set, `IsTestMode`.
    - `Defaults_are_null`: `[]` → both null.
  - `MainFormTests` (STA, off-screen, fake snapshot source returning a mutable list, `FakeInfo`, fake icon loader, `FakeTimer`, temp-file `SettingsStore`):
    - `First_sample_populates_details_rows`: show → `Details.DisplayedRows.Count == 3`.
    - `Speed_menu_command_changes_clock`: send `WM_COMMAND` with `CommandIds.SpeedLow` → `Clock.Speed == Low`, and `FakeTimer.IntervalMs == 4000`.
    - `Paused_takes_no_snapshots`: send `SpeedPaused`; `FakeTimer.Fire()` × 3 → the snapshot source's read count is unchanged.
    - `Always_on_top_command_toggles_TopMost`.
    - `Exited_process_releases_icon_while_minimized`: minimize; remove a row from the fake snapshot; `Clock.RefreshNow()` → `Icons.LiveCount` drops by 1 and `Table.Count == 2`.
    - `Closing_saves_settings`: change speed to Low, close → the temp settings file loads with `UpdateSpeed == Low`.
- [ ] **Step 2: Run the tests to confirm they fail.** Run `dotnet test --filter "MainFormTests|LaunchOptionsTests"`. Expect FAIL.
- [ ] **Step 3: Implement** the wiring above.
- [ ] **Step 4: Run the whole suite.** Run `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: main window wiring, native menus, single instance, launch options`.

---

### Task 12: Diagnostics, scenario mode, churn and soak tools

**Files:**
- Create: `src/Tmcs/Diagnostics/DiagSnapshot.cs`, `DiagLog.cs`, `ScenarioParser.cs`, `ScenarioRunner.cs`
- Modify: `src/Tmcs/MainForm.cs`, which implements `IScenarioTarget` and starts `DiagLog` / `ScenarioRunner` in test mode
- Create: `tools/churn.ps1`, `tools/soak-report.ps1`, `tools/fixtures/soak-pass.csv`, `tools/fixtures/soak-fail.csv`
- Test: `tests/Tmcs.Tests/Diagnostics/DiagnosticsTests.cs`

**Interfaces:**
- Consumes: `ISampleTimer` (Task 8); `MainForm` hooks (Task 11).
- Produces:
  - `enum ScenarioAction { Details, Minimized, Paused }`. Each action sets the full state: Details means restored, Details tab, unpaused; Minimized means unpaused and minimized; Paused means restored, Details tab, paused.
  - `readonly record struct ScenarioStep(ScenarioAction Action, TimeSpan Duration)`
  - `static class ScenarioParser { static IReadOnlyList<ScenarioStep> Parse(string spec); }`: the format is `action:minutes[,action:minutes]*`, where minutes may be fractional and parsed with the invariant culture. It throws `FormatException` on bad input.
  - `interface IScenarioTarget { void Apply(ScenarioAction a); }`
  - `sealed class ScenarioRunner`:
    - Constructor: `ScenarioRunner(IReadOnlyList<ScenarioStep> steps, IScenarioTarget target, ISampleTimer stepTimer, Action finished)`.
    - Members: `string CurrentStep` (lowercase action name, or `"done"`), `double LastSwitchMs` (a `Stopwatch` around `Apply`), `void Start()`.
  - `readonly record struct DiagSnapshot(DateTime Utc, string Step, long Samples, int Rows, int IconSlots, int IconLive, int IconPooled, int SidCache, long PrivateBytes, long ManagedBytes, int Handles, int Gdi, int User, double CpuMs, double SwitchMs)`
  - `sealed class DiagLog : IDisposable`:
    - Constructor: `DiagLog(string csvPath, Func<DiagSnapshot> probe, ISampleTimer timer)`. The timer runs at 10 s, independent of the sample clock.
    - `const string Header = "utc,step,samples,rows,icon_slots,icon_live,icon_pooled,sid_cache,private_bytes,managed_bytes,handles,gdi,user,cpu_ms,switch_ms"`.
    - `static string Format(DiagSnapshot s)`: invariant culture, ISO-8601 UTC.
  - Test-mode `MainForm` behavior:
    - Skips the single instance check.
    - Shows without activation and moves to the bottom of the z-order (`SetWindowPos(HWND_BOTTOM, SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE)`).
    - Doesn't save settings.
    - Exits when the scenario finishes.
    - The probe reads `Process.GetCurrentProcess()` (`Refresh` → `PrivateMemorySize64`, `HandleCount`, `TotalProcessorTime`), `GC.GetTotalMemory(false)` and `GetGuiResources(GetCurrentProcess(), 0 / 1)`.
  - `tools/churn.ps1 -Parallel 8 -Minutes 60`:
    - Keeps `Parallel` concurrent `cmd.exe /c ping -n 2 127.0.0.1` processes alive, started with `UseShellExecute=false`, `CreateNoWindow=true`, so no windows appear.
    - Replaces each as it exits and disposes the handles.
    - Prints the total spawned at the end.
  - `tools/soak-report.ps1 -Csv <path> [-WarmupMinutes 2]`:
    - Splits rows into contiguous step segments and drops each segment's first `WarmupMinutes`.
    - Per segment it checks:

      | Check | Pass when |
      |---|---|
      | private bytes growth (max of last quarter − max of first quarter) | ≤ 2 MB |
      | gdi range | ≤ 5 |
      | user range | ≤ 5 |
      | handles range | ≤ 50 |
      | paused segments: samples delta | == 0 |
      | first row's `switch_ms` | ≤ 100 |

    - Prints a table with PASS/FAIL per check and exits 0 if everything passes, otherwise 1.

- [ ] **Step 1: Write the failing tests.**
  - `Parse_scenario_spec`: `"details:10,minimized:0.5,paused:5"` → three steps with durations 10 min, 30 s and 5 min. `"bogus:1"` and `"details"` throw `FormatException`.
  - `Runner_applies_steps_in_order_and_finishes`: a fake target records actions. A `FakeTimer` with `Fire()` advances steps: `Start()` applies step 0; each `Fire()` applies the next; after the last, `finished` is invoked and `CurrentStep == "done"`.
  - `Diag_format_is_invariant_csv`: a known snapshot → the exact expected line, for example `2026-10-23T12:00:00.0000000Z,details,5,3,4,2,1,1,1048576,524288,300,40,20,12.5,3.25`. The header has 15 fields.
- [ ] **Step 2: Run the tests to confirm they fail.** Run `dotnet test --filter DiagnosticsTests`. Expect FAIL.
- [ ] **Step 3: Implement** the types, the test-mode wiring and both scripts. Create the fixture CSVs: `soak-pass.csv` is flat; `soak-fail.csv` has private bytes rising 1 MB per minute and GDI rising 1 per row.
- [ ] **Step 4: Verify.**
  - Run `dotnet test`. Expect all PASS.
  - Run `pwsh -File tools/soak-report.ps1 -Csv tools/fixtures/soak-pass.csv`. Expect all PASS and exit code 0.
  - Run `pwsh -File tools/soak-report.ps1 -Csv tools/fixtures/soak-fail.csv`. Expect private bytes and gdi FAIL, and exit code 1.
  - Run `pwsh -File tools/churn.ps1 -Parallel 2 -Minutes 0.2`. Expect a printed spawn count above 10 and no windows appearing.
- [ ] **Step 5: Commit** with message `feat: diag log, self-driving scenario mode, churn and soak tools`.

---

### Task 13: Publish and milestone-1 acceptance

**Files:**
- Create: `src/Tmcs/Properties/PublishProfiles/win-x64.pubxml`
- Modify: `README.md` (publish, sign and soak instructions)
- Create: `docs/superpowers/results/2026-10-23-m1-soak.md`

**Interfaces:**
- Consumes: the whole app; `tools/churn.ps1` and `tools/soak-report.ps1` (Task 12).

- [ ] **Step 1: Publish profile.** Settings: `Configuration Release`, `RuntimeIdentifier win-x64`, `SelfContained true`, `PublishSingleFile true`, `IncludeNativeLibrariesForSelfExtract true`, `PublishDir ..\..\artifacts\publish\`. Run `dotnet publish src/Tmcs -p:PublishProfile=win-x64`. Expect `artifacts/publish/tmcs.exe` and no other `.dll` files beside it. Check that `(Get-Item artifacts/publish/tmcs.exe).VersionInfo` shows ProductName `tmcs` and CompanyName `ILW8`.
- [ ] **Step 2: README.** Add the publish command, a signing example (`signtool sign /fd SHA256 /a /tr <timestamp-url> /td SHA256 artifacts\publish\tmcs.exe`, with "use your own certificate"), the soak procedure, and the note that Release builds require admin.
- [ ] **Step 3: Non-elevated soak (Debug build, no UAC).**
  - Tell the owner in chat first: a tmcs window will sit behind their other windows for about 50 minutes and won't take focus. Wait for their OK.
  - Run `dotnet build -c Debug`.
  - Start `pwsh -File tools/churn.ps1 -Parallel 8 -Minutes 52` in the background.
  - Run `src/Tmcs/bin/Debug/net10.0-windows/win-x64/tmcs.exe --diag artifacts/soak/m1-debug.csv --scenario "details:10,minimized:30,details:3,paused:5,details:2"` in the background.
  - When tmcs exits, run `pwsh -File tools/soak-report.ps1 -Csv artifacts/soak/m1-debug.csv`. Expect exit code 0.
  - The `minimized:30 → details` switch covers spec test 2, no backlog stall. The paused segment covers spec test 3, true pause.
- [ ] **Step 4: Elevated soak (published build).**
  - Ask the owner in chat for OK to show one UAC prompt.
  - Then run `Start-Process artifacts/publish/tmcs.exe -Verb RunAs -ArgumentList '--diag', "$PWD/artifacts/soak/m1-elevated.csv", '--scenario', 'details:10,minimized:10,details:3,paused:5,details:2'` with churn running.
  - Run `soak-report.ps1` on the CSV. Expect exit code 0.
- [ ] **Step 5: Idle cost (spec test 4).**
  - From the elevated CSV's `details` segments, compute tmcs CPU ms per minute.
  - Ask the owner to put the stock Task Manager on the Details tab at Normal speed for 5 minutes. Then measure `(Get-Process Taskmgr).TotalProcessorTime` before and after.
  - Expect tmcs ≤ Task Manager, and record both numbers.
- [ ] **Step 6: Record results.** Write `docs/superpowers/results/2026-10-23-m1-soak.md` with the churn spawn count, the per-segment report tables, the idle-cost numbers, and any failure with its fix. On any FAIL, stop, use superpowers:systematic-debugging, fix, and re-run the failed soak before continuing.
- [ ] **Step 7: Owner fidelity check (spec test 6).** Ask the owner to launch `artifacts/publish/tmcs.exe` themselves and compare the Details tab with Task Manager side by side: columns, formats, alignment, sort arrows, header menu, Select columns dialog and icons. Record their notes in the results file. Fidelity fixes become follow-up tasks, not silent changes.
- [ ] **Step 8: Commit** with message `chore: publish profile, readme, milestone 1 soak results`. Run `git add` on the profile, the README and the results file. `artifacts/` stays untracked. Don't push without the owner's OK.
