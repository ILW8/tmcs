# tmcs Milestone 2 (Performance tab) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Task Manager's Performance tab to tmcs: a sidebar of devices with live mini graphs, and a pane per device (CPU, Memory, Disk, Network, GPU) with 60 seconds of graphs and the numbers below them. Then prove with the spec's acceptance tests that memory and handle counts stay flat on every pane.

**Architecture:**
- Nothing moves off the UI thread. Each `SampleClock` tick still reads the process snapshot. It now also reads every device's counters, on every tab and while minimized, through a `PerformanceModel`. The model appends one point per tick to each device's fixed-size, time-stamped `History`.
- Rendering is separate from sampling. The Performance view only invalidates its sidebar and the visible pane, and only while it can be seen. Becoming visible repaints from the current histories, so there is never a backlog.
- Data comes through small source interfaces (`ICpuSource`, `IMemorySource`, `IDiskSource`, `INetworkSource`, `IGpuSource`), each with a real Win32 implementation and a fake in the tests. Rates and percentages are computed from counter deltas in plain, tested classes.
- Every graph is painted by one `GraphPainter` per device palette, whose pens and brushes are created once. The 32 CPU graphs are one owner-drawn control, and the sidebar is one owner-drawn, scrollable control. Neither creates a control or GDI object per entry.
- Test mode gains the scenario steps `cpu`, `network` and `gpu`, and `paused` now pauses on the CPU pane, so the soak can hold each pane for the spec's minimum time. A small `gpu-touch` tool lets the churn create GPU processes.

**Tech Stack:** C# 14 / .NET 10, WinForms, `LibraryImport` P/Invoke (ntdll, kernel32, iphlpapi, wlanapi, gdi32 `D3DKMT*`, setupapi), PDH where the data-sources reference says so, xUnit 2.9.3, PowerShell 7 for tools.

**Spec:** `docs/superpowers/specs/2026-10-23-tmcs-design.md` (approved), sections 4.1, 4.3, 5, 6 and 9, as amended on 2026-10-24.

**Reference docs (read before the task that names them):**
- `docs/superpowers/reference/2026-10-24-taskmgr-performance-look.md`: the stock Performance tab's geometry, fonts, colors and text formats, measured from the owner's captures.
- `docs/superpowers/reference/2026-10-24-performance-data-sources.md`: the CPU, memory, disk and network APIs, verified on this machine.
- `docs/superpowers/reference/2026-10-24-gpu-data-sources.md`: the D3DKMT GPU calls, verified on this machine.
- The captures themselves are in `artifacts/reference/taskmgr/` (git-ignored; never commit them). Each pane has a `.png` and a `.txt` element dump with bounds.

**Milestone order and the early stop:** Tasks 1–13 build every device's data, the tab, the sidebar and the CPU pane. **Task 14 stops execution** so the owner can try them before the Memory, Disk, Network and GPU panes are built (the owner's choice, 2026-10-24). Tasks 15–18 build those panes, Task 19 adds what the soak needs, and Task 20 runs the acceptance tests once, for the whole milestone.

## Global Constraints

- Everything in milestone 1's Global Constraints still holds (`docs/superpowers/plans/2026-10-23-tmcs-m1-details.md`): framework and platform properties, names, version-resource identity, manifests, P/Invoke from System32 only, identity-keyed process state.
- Tabs, in this order: `Performance`, `Details`. tmcs opens on `Performance`, always (spec 4.1 and 12).
- Sidebar entries, in this order: `CPU`, `Memory`, `Disk 0` … `Disk N` (labelled with their drive letters), every network adapter, `GPU 0` … (only real GPUs; no DisplayLink, Parsec or other indirect-display or software adapters).
- Graphs cover **60 seconds** of history in fixed-size buffers. A buffer holds 121 points, enough for 60 s at High speed (0.5 s), and never grows.
- Sampling continues on every tab and while minimized. **Paused stops everything**: no snapshot, no counter reads, no timers firing. No graph, pane or sidebar may run a timer of its own. Refresh now (F5) takes exactly one sample, even while paused.
- Switching tabs, switching panes, restoring the window and un-pausing take under about **100 ms**. Views repaint from the current histories.
- Graphs are owner-drawn into double-buffered controls. Pens, brushes and fonts are created once per control or painter and reused. GDI and USER object counts stay flat. No control, window or GDI object per graph, per sidebar entry, per sample or per process.
- Per-device state is keyed by a stable device key (see Task 10). It is dropped on the sample whose rescan finds the device gone, whatever is showing, and the sidebar lets go of it on that same sample.
- CPU pane: 32 graphs, one per logical processor, in Task Manager's grid. Each also draws kernel time, always, with no menu (owner, 2026-10-24). The Utilization number follows `% Processor Utility`, not `% Processor Time` (spec 6, verified 2026-10-24). The Details CPU column is unchanged.
- GPU: the temperature shows in the pane as `GPU Temperature` and in the sidebar entry as `(42 °C)` (owner, 2026-10-24).
- Hiding is for network adapters only. Hidden adapters are remembered in `AppSettings.HiddenAdapters`, keyed by the adapter's interface GUID (`{xxxxxxxx-…}`, lowercase, with braces), never its name.
- **Out of scope** (spec 4.3 and 12): Task Manager's sidebar menu (Summary view, Show/Hide graphs, Copy); the pane's right-click menu (Change graph to, Show kernel times, Graph summary view, View, View network details, Copy); "Open Resource Monitor"; "Fewer details"; the GPU engine dropdowns (the four engine graphs are fixed to 3D, Copy, Video Encode, Video Decode).
- Text, sizes and colors come from the look reference. Layout is in 96-DPI units scaled by `DeviceDpi / 96`. Fonts are recreated on a DPI change, not per paint.
- **Owner rules:**
  - Never use computer use, screenshots or UI automation of the owner's desktop. Test windows use the off-screen helpers (`OffscreenForm`, `OffscreenMainForm`): off-screen, no activation, no taskbar or Alt-Tab entry.
  - Before any UAC prompt or anything the owner must do on screen, tell them and wait for "ready", even if they approved it earlier.
  - Never trigger a UAC prompt, change system settings, or `git push` without the owner's OK in chat at the time.
  - Never add a feature, tab, column, menu item or setting the spec doesn't list. Ask first.
  - Test and diagnostic knobs are `TMCS_*` environment variables, never command-line flags.
  - Use the Rider MCP inspections (`lint_files`, `get_file_problems`) when reviewing. Analysis only: no screenshots, no launching the app from Rider.
- Commits: author `ILW8 <ilw8@41f.dev>` on branch `feat/tmcs-m2`. End every commit message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. `artifacts/` is never committed.

## Review Focus

The spec doesn't name these, but each is likely to happen on the owner's machine. Each line names the task that owns its tests.

1. **Adapters coming and going while tmcs runs.** Tailscale goes down and up, Hyper-V re-creates a vSwitch, a USB Wi-Fi adapter is unplugged, DHCP renews an address.
   - The sidebar follows within one sample.
   - Surviving adapters keep their graphs.
   - If the selected adapter's pane is showing when the adapter goes, the selection falls back on the same sample.
   - Tests: Task 10 (`Adapter_change_keeps_the_history_and_updates_the_info`) and Task 12 (`Removed_selected_device_falls_back_on_the_same_sample`).
2. **A GPU driver update or restart.** It changes the adapter's LUID, and so its key.
   - The failed read triggers a rescan, and GPU 0 comes back under its new key.
   - If it was selected, it stays selected.
   - Tests: Task 10 (`Gpu_luid_change_replaces_the_gpu`) and Task 12 (`Selection_follows_a_gpu_whose_luid_changed`).
3. **Long uptimes wrap 32-bit counters.** The owner's PC was at 25 days. `DISK_PERFORMANCE`'s `ReadCount` and `WriteCount` are 32-bit and can wrap within weeks of heavy I/O.
   - The response time keeps working through the wrap, from an unsigned delta.
   - The wrap is neither a reset nor a spike.
   - Test: Task 7 (`Io_counts_wrapping_past_uint_max_keep_working`).
4. **Moving the window to a monitor with another scale.** tmcs is PerMonitorV2.
   - Fonts are recreated once per change and the layout scales.
   - GDI objects stay flat however often it happens.
   - Test: Task 11 (`Dpi_changes_recreate_fonts_and_keep_gdi_flat`).
5. **Shrinking the window to its minimum size (400×300) or smaller.** Graph rectangles can come out empty or negative.
   - Nothing throws; empty graphs are skipped.
   - Tests: Task 4 (`Tiny_bounds_paint_nothing_and_do_not_throw`, added in Task 11) and Task 11 (`Painting_at_tiny_sizes_does_not_throw`, which covers every pane as it is built).

---

### Task 1: M1 follow-ups: ignore startup hooks, tests on comctl32 v6

The owner picked these two of the milestone 1 follow-ups (`docs/superpowers/results/2026-10-23-m1-soak.md`) for M2. The others stay parked.

**Files:**
- Modify: `src/Tmcs/Tmcs.csproj`
- Create: `tests/Tmcs.Tests/Support/CommonControls6.cs`, `tests/Tmcs.Tests/Support/comctl6.manifest`
- Modify: `tests/Tmcs.Tests/Support/Sta.cs`, `tests/Tmcs.Tests/Tmcs.Tests.csproj` (copy the manifest to the output)
- Test: `tests/Tmcs.Tests/ManifestTests.cs`, `tests/Tmcs.Tests/Support/StaTests.cs`

**Interfaces:**
- Produces: `Sta.Run(Action)` runs its body with a comctl32 v6 activation context active on the STA thread, so every window the body creates and every comctl32 P/Invoke it first binds come from comctl32 6.x, as in the app.
- Produces: `internal static class CommonControls6 { static IDisposable Activate(); }`. It builds one activation context per process from `comctl6.manifest` (the same `Microsoft.Windows.Common-Controls` 6.0.0.0 dependency as `app.manifest`) with `CreateActCtxW`, and `Activate` returns a scope that calls `ActivateActCtx` now and `DeactivateActCtx` on dispose. The context is never released; it lives as long as the test process.

- [ ] **Step 1: Write the failing tests.**
  - `ManifestTests.Startup_hooks_are_disabled`: load `src/Tmcs/Tmcs.csproj` as XML (find it as `Load` finds the manifests) and assert that a `StartupHookSupport` property exists with the value `false`.
  - `StaTests.List_view_on_an_Sta_thread_comes_from_comctl32_v6`: inside `Sta.Run`, show an `OffscreenForm` holding a `ListView`; `GetClassLongPtrW(list.Handle, GCLP_HMODULE = -16)` gives the module that registered the class; `GetModuleFileNameW` on it; `FileVersionInfo.GetVersionInfo(path).FileMajorPart == 6`.
  - `StaTests.Tmcs_comctl32_imports_bind_to_the_list_view_module`: inside `Sta.Run`, `NativeLibrary.Load("comctl32.dll", typeof(MainForm).Assembly, null)` equals the list view's class module from the previous test. That is the library a `LibraryImport("comctl32.dll")` in tmcs binds to.
- [ ] **Step 2: Run them.** Run `dotnet test --filter "ManifestTests|StaTests"`. Expect the three new tests to FAIL (no property; class module is comctl32 5.82).
- [ ] **Step 3: Implement.** Add `<StartupHookSupport>false</StartupHookSupport>` to the first `PropertyGroup` of `Tmcs.csproj`, with a comment: tmcs runs elevated with the user's environment, so `DOTNET_STARTUP_HOOKS` must not load code into it. Add `CommonControls6`, the manifest (copied to the output with `CopyToOutputDirectory PreserveNewest`), and in `Sta.Run` wrap the body in `using (CommonControls6.Activate())`, after the `SetUnhandledExceptionMode` call, which must stay first.
- [ ] **Step 4: Run the whole suite.** Run `dotnet test`. Expect all PASS. Tests that relied on comctl32 5.82 behavior may now fail: the header sort-arrow test, the theming test and the Task 12b pixel test are the likely ones. Fix each test's expectation to what comctl32 v6 (the app's version) does, never the production code to suit v5, and list every such change in the commit message.
- [ ] **Step 5: Check the built runtime config.** Run `dotnet build src/Tmcs -c Debug`, then check that `src/Tmcs/bin/Debug/net10.0-windows/win-x64/tmcs.runtimeconfig.json` contains `"System.StartupHookProvider.IsSupported": false`.
- [ ] **Step 6: Commit** with message `fix: ignore startup hooks; run the test host's windows on comctl32 v6`.

---

### Task 2: History buffer and the device base class

**Files:**
- Create: `src/Tmcs/Performance/History.cs`, `src/Tmcs/Devices/Device.cs`
- Test: `tests/Tmcs.Tests/Performance/HistoryTests.cs`

**Interfaces:**
- Produces: `internal enum DeviceKind { Cpu, Memory, Disk, Network, Gpu }`, in sidebar order.
- Produces: `internal abstract class Device(string key, DeviceKind kind, int seriesCount)`, one sidebar entry's data, in `Tmcs.Devices`:
  - `string Key` (stable, see Task 10), `DeviceKind Kind`, `History History` (created with `seriesCount`).
  - `abstract string Title`: the sidebar title, e.g. `CPU`, `Disk 0 (C:)`, `Ethernet`, `GPU 0`.
  - `virtual string PaneTitle => Title`: the pane heading's left text (`GPU` for a GPU).
  - `abstract string Subtitle`: the pane heading's right text, e.g. the processor name, `64.0 GB`, the disk model, the adapter description, the GPU name.
  - `abstract void Sample(long time100Ns, IReadOnlyList<RawProcess> snapshot)`: reads the device's counters once and appends one point to `History`. Only CPU and Memory use the snapshot.
  - `virtual bool Failed => false`: true when the latest read failed in a way that means the device is gone or changed (the model re-enumerates that kind on the next tick, Task 10).
- Produces: `internal sealed class History`, 60 seconds of time-stamped points for one device, with one shared time column and `SeriesCount` value columns in a fixed ring:
  - `const int Capacity = 121` (60 s at High speed, plus the point at the left edge).
  - `const long Window100Ns = 590_000_000` (59 s). Task Manager spreads 60 samples from border to border with the newest on the right border (look reference, section 4), so at Normal speed this window puts every point exactly where Task Manager draws it.
  - `History(int seriesCount)`; `int SeriesCount`; `int Count` (≤ `Capacity`); `long LatestTime` (100 ns units, 0 when empty).
  - `void Add(long time100Ns, ReadOnlySpan<float> values)`: `values.Length` must equal `SeriesCount` (else `ArgumentException`). A time not after `LatestTime` is ignored. When full, the oldest point is overwritten.
  - `long TimeAt(int index)` and `float ValueAt(int series, int index)`: index 0 is the oldest point kept.
  - `int FirstVisible`: the index of the newest point at or before `LatestTime − Window100Ns`, or 0 if there is none. A graph draws from there, so its line enters at the left edge.
  - `long SequenceAt(int index)`: how many points were added before this one since the history was created. Graph grid lines sit on every `s`-th point by this number, so they scroll with the data (Task 4).
  - `float MaxVisible(int series)`: the largest value from `FirstVisible` on; 0 when empty.
- Graphs are time-based: the right edge is `LatestTime`, the left edge `Window100Ns` before it. At Normal speed that is Task Manager's 60 samples exactly. At High it shows 119 points and at Low 16 over the same span, and it freezes while paused, because no point is added. Task Manager's graphs are sample-based (60 samples whatever the speed); the time-based span at High and Low is my call, because the captions say "60 seconds". Note it for the owner's fidelity check.

- [ ] **Step 1: Write the failing tests.**

```csharp
static History Filled(int points, long step100Ns, int series = 1)
{
    var h = new History(series);
    Span<float> v = stackalloc float[series];
    for (var i = 1; i <= points; i++) { v.Fill(i); h.Add(i * step100Ns, v); }
    return h;
}

[Fact] public void Add_keeps_order_and_caps_at_capacity()
{ var h = Filled(130, 5_000_000);
  Assert.Equal(History.Capacity, h.Count); Assert.Equal(10 * 5_000_000L, h.TimeAt(0));
  Assert.Equal(130 * 5_000_000L, h.LatestTime); Assert.Equal(130f, h.ValueAt(0, h.Count - 1));
  Assert.Equal(9, h.SequenceAt(0)); Assert.Equal(129, h.SequenceAt(h.Count - 1)); }

[Fact] public void First_visible_is_the_newest_point_at_or_before_59_s_ago()
{ var h = Filled(100, 10_000_000); // one point a second, 100 s
  Assert.Equal(41 * 10_000_000L, h.TimeAt(h.FirstVisible));
  Assert.Equal(60, h.Count - h.FirstVisible); } // Task Manager's 60 samples

[Fact] public void First_visible_is_0_when_history_is_shorter_than_the_window()
{ Assert.Equal(0, Filled(10, 10_000_000).FirstVisible); }

[Fact] public void Series_are_kept_apart()
{ var h = new History(2); h.Add(1, [1f, 10f]); h.Add(2, [2f, 20f]);
  Assert.Equal(20f, h.ValueAt(1, 1)); Assert.Equal(1f, h.ValueAt(0, 0)); }

[Fact] public void Max_visible_ignores_points_older_than_the_window()
{ var h = new History(1); h.Add(1, [99f]);
  for (var s = 1; s <= 70; s++) h.Add(1 + s * 10_000_000L, [s]);
  Assert.Equal(70f, h.MaxVisible(0)); Assert.True(h.TimeAt(h.FirstVisible) > 1); }

[Fact] public void Time_not_after_latest_is_ignored()
{ var h = new History(1); h.Add(10, [1f]); h.Add(10, [2f]); h.Add(5, [3f]);
  Assert.Equal(1, h.Count); Assert.Equal(1f, h.ValueAt(0, 0)); }

[Fact] public void Wrong_value_count_throws()
{ Assert.Throws<ArgumentException>(() => new History(2).Add(1, [1f])); }

[Fact] public void Add_does_not_allocate()
{ var h = Filled(200, 5_000_000, series: 3); Span<float> v = stackalloc float[3];
  var before = GC.GetAllocatedBytesForCurrentThread();
  for (var i = 0; i < 1000; i++) h.Add(2_000_000_000L + i, v);
  Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread()); }
```
- [ ] **Step 2: Run them.** Run `dotnet test --filter HistoryTests`. Expect FAIL: `History` not defined.
- [ ] **Step 3: Implement** `History` with a `long[Capacity]` time ring and a `float[SeriesCount * Capacity]` value ring, a head index and a count. Add `DeviceKind` and the `Device` base; the device tasks test their subclasses.
- [ ] **Step 4: Run them.** Expect PASS (8 tests).
- [ ] **Step 5: Commit** with message `feat: fixed-size, time-stamped history and the device base class`.

---

### Task 3: Performance value formats

Read the "Formats" section of the data-sources reference first. Every example below is a value Task Manager showed in the owner's captures.

**Files:**
- Create: `src/Tmcs/Performance/PerfFormat.cs`
- Test: `tests/Tmcs.Tests/Performance/PerfFormatTests.cs`

**Interfaces:**
- Produces `internal static class PerfFormat`. Numbers use the current culture's decimal separator and no group separator. Sizes are binary (KB = 2^10 … PB = 2^50).
  - `string Number(double value)`: the shared precision rule. Exactly 0 → `"0"`. Otherwise round half away from zero to one decimal; if that is ≥ 100, round it again to a whole number and show no decimals; else show one decimal. (The double rounding is how 931.496 GiB shows "932 GB".)
  - `string Size(double bytes)`: `Number` in the largest of KB, MB, GB, TB, PB whose value is ≥ 1 (KB below 1 KB), then a space and the unit.
  - `string Gigabytes(double bytes)`: `Number` in GB, then ` GB`.
  - `string Pair(double a, double b, string unit, double unitBytes)`: `a/b unit`, both in `unitBytes` units, both with the precision `Number` would give the larger one.
  - `string DiskRate(double bytesPerSecond)`: `KB/s` below 1 MB/s, else `MB/s` below 1 GB/s, else `GB/s`.
  - `double QuantizeBits(double bitsPerSecond)`: Task Manager's rates are whole kilobytes a second times 8: `Math.Round(bitsPerSecond / 8 / 1000, MidpointRounding.AwayFromZero) * 8000`. K = 1000 is my call (the captures can't tell 1000 from 1024).
  - `string BitRate(double bitsPerSecond)`: quantize, then `Kbps` below 1 Mbps (0 shows `0 Kbps`), `Mbps` below 1 Gbps, else `Gbps`; decimal units.
  - `(string Send, string Receive) SidebarRates(double sendBits, double receiveBits)`: the two values of the sidebar's `S: x R: y unit` line. Both are quantized and share one unit, which the larger value chooses as `BitRate` would. Only `Receive` carries the unit. Each number keeps its own precision. The sidebar draws `S: ` and `R: ` itself, in gray (Task 12).
  - `string Percent(double percent)`: rounded half away from zero, then `%` with no space. Rounding fits the CPU capture best (45 of 60 ticks, against 24 for truncation).
  - `string Ghz(double mhz)`: two decimals, then ` GHz`.
  - `string UpTime(TimeSpan t)`: `d:hh:mm:ss`, days unpadded, seconds floored.
  - `string Milliseconds(double ms)`: `Number`, then ` ms`.
  - `string Celsius(double degrees)`: rounded half away from zero, then ` °C` (my call; the GPU reference couldn't tell rounding from truncation).

- [ ] **Step 1: Write the failing tests** (set `CultureInfo.CurrentCulture` to `en-US` in each test):

```csharp
const double KiB = 1024, MiB = KiB * 1024, GiB = MiB * 1024;

[Theory]
[InlineData(1000204886016.0, "932 GB")]          // Disk capacity, 931.51 GiB
[InlineData(931.496 * GiB, "932 GB")]            // Disk 2's formatted size: rounded to 931.5 first
[InlineData(931.386 * GiB, "931 GB")]            // the formatted size of disks 0 and 1
[InlineData(2000398934016.0, "1.8 TB")]
[InlineData(356.5 * MiB, "357 MB")]              // hardware-reserved memory
[InlineData(1 * MiB, "1.0 MB")]                  // the L1 cache
[InlineData(64 * MiB, "64.0 MB")]
[InlineData(512 * KiB, "512 KB")]
public void Size_picks_the_largest_unit_and_the_precision_rule(double bytes, string expected) =>
    Assert.Equal(expected, PerfFormat.Size(bytes));

[Fact] public void Number_shows_zero_bare_and_drops_decimals_from_100() {
    Assert.Equal("0", PerfFormat.Number(0)); Assert.Equal("1.6", PerfFormat.Number(1.6));
    Assert.Equal("100", PerfFormat.Number(99.96)); Assert.Equal("216", PerfFormat.Number(216.4)); }

[Fact] public void Pair_shares_the_larger_values_precision() {
    const double G = 1L << 30;
    Assert.Equal("55/120 GB", PerfFormat.Pair(54.7 * G, 119.7 * G, "GB", G));
    Assert.Equal("38.4/63.7 GB", PerfFormat.Pair(38.4 * G, 63.7 * G, "GB", G)); }

[Fact] public void Disk_rates() {
    Assert.Equal("0 KB/s", PerfFormat.DiskRate(0)); Assert.Equal("1.3 MB/s", PerfFormat.DiskRate(1.3 * 1048576));
    Assert.Equal("708 MB/s", PerfFormat.DiskRate(708 * 1048576.0)); }

[Fact] public void Bit_rates_are_whole_kilobytes_times_8() {
    Assert.Equal("40.0 Kbps", PerfFormat.BitRate(40_300)); Assert.Equal("0 Kbps", PerfFormat.BitRate(0));
    Assert.Equal("120 Kbps", PerfFormat.BitRate(119_000)); Assert.Equal("216 Mbps", PerfFormat.BitRate(216_000_000));
    Assert.Equal(16_000, PerfFormat.QuantizeBits(16_700)); }

[Fact] public void Sidebar_rates_share_one_unit() {
    Assert.Equal(("8.0", "40.0 Kbps"), PerfFormat.SidebarRates(8_000, 40_000));
    Assert.Equal(("0", "8.0 Kbps"), PerfFormat.SidebarRates(0, 8_000));
    Assert.Equal(("0.3", "21.7 Mbps"), PerfFormat.SidebarRates(300_000, 21_700_000));
    Assert.Equal(("1.4", "216 Mbps"), PerfFormat.SidebarRates(1_400_000, 216_000_000)); }

[Fact] public void Ten_gigabit_rates() { // the owner's NC550SFP is 10GbE
    Assert.Equal("9.4 Gbps", PerfFormat.BitRate(9_400_000_000));
    Assert.Equal(("0.1", "9.4 Gbps"), PerfFormat.SidebarRates(100_000_000, 9_400_000_000)); }

[Fact] public void Decimal_separator_follows_the_culture() {
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
    Assert.Equal("38,4/63,7 GB", PerfFormat.Pair(38.4 * GiB, 63.7 * GiB, "GB", GiB));
    Assert.Equal("4,42 GHz", PerfFormat.Ghz(4420)); }

[Fact] public void Cpu_and_misc_formats() {
    Assert.Equal("10%", PerfFormat.Percent(9.5)); Assert.Equal("6%", PerfFormat.Percent(6.24));
    Assert.Equal("4.42 GHz", PerfFormat.Ghz(4420)); Assert.Equal("4.50 GHz", PerfFormat.Ghz(4500));
    Assert.Equal("25:17:28:41", PerfFormat.UpTime(new TimeSpan(25, 17, 28, 41, 900)));
    Assert.Equal("0:00:00:05", PerfFormat.UpTime(TimeSpan.FromSeconds(5)));
    Assert.Equal("0 ms", PerfFormat.Milliseconds(0)); Assert.Equal("38 °C", PerfFormat.Celsius(37.8)); }
```
- [ ] **Step 2: Run them.** Run `dotnet test --filter PerfFormatTests`. Expect FAIL: `PerfFormat` not defined.
- [ ] **Step 3: Implement** `PerfFormat`.
- [ ] **Step 4: Run them.** Expect PASS.
- [ ] **Step 5: Commit** with message `feat: Task Manager's number formats for the performance tab`.

---

### Task 4: Graph palettes and the graph painter

Read sections 3 and 4 of the look reference first.

**Files:**
- Create: `src/Tmcs/Performance/Graphs/GraphPalette.cs`, `src/Tmcs/Performance/Graphs/GraphPainter.cs`
- Test: `tests/Tmcs.Tests/Performance/GraphPainterTests.cs`

**Interfaces:**
- Consumes: `History` (Task 2).
- Produces: `internal sealed record GraphPalette(Color Border, Color TopCorner, Color BottomCorner, Color Grid, Color Line, Color Fill, Color GridInFill, Color SecondLine, Color SecondFill, Color GridInSecondFill)`. A `SecondFill` of `Color.Empty` means the second series is a dashed line with no fill.
- Produces: `internal static class GraphPalettes` with these values, verbatim from the look reference (hex RGB):

  | Palette | Border | Top / bottom corners | Grid | Line | Fill | Grid in fill | Second line | Second fill | Grid in second fill |
  |---|---|---|---|---|---|---|---|---|---|
  | `Cpu` | 117DBB | 3A94C7 / 1F85BF | D9EAF4 | 117DBB | F1F6FA | CEE2F0 | 81B3D2 (kernel) | D9E7F1 | BAD6E8 |
  | `Memory` | 8B12AE | 9F3BBB / 9220B3 | ECDEF0 | 9528B4 | F4F2F4 | E3D4E6 | – | – | – |
  | `Disk` | 4DA60C | 6CB536 / 58AC1A | DBEDCE | 4DA60C (read) | EFF7E9 | CEE7BC | 79D50D (write, dashed) | empty | – |
  | `Network` | A74F01 | B66D2D / AD5A10 | EEDECF | A74F01 (receive) | FCF3EB | EDD5C0 | DF672C (send, dashed) | empty | – |
  | `Gpu` | as `Cpu`, with no second series | | | | | | | | |

- Produces: `internal sealed class GraphPainter(GraphPalette palette) : IDisposable`. It creates every pen, brush and path it needs once, in the constructor, and reuses a point buffer of `History.Capacity + 2` points, so painting allocates nothing.
  - `GraphPalette Palette`.
  - `void Paint(Graphics g, Rectangle bounds, History history, int series, float max, int gridSamples, int second = -1)`: paints one graph whose border pixels are the outer pixels of `bounds`. Values are clamped to `[0, max]`; `max <= 0` paints only the frame. `gridSamples` 0 means no grid at all (the sidebar's mini graphs). `second` is the series drawn with the palette's second style (CPU kernel time, disk write, network send), or -1.
- Drawing rules (look reference section 4). Let `left = bounds.X`, `right = bounds.Right − 1`, `top = bounds.Y`, `bottom = bounds.Bottom − 1`:
  - Point x = `right − (history.LatestTime − t) / History.Window100Ns × (right − left)`; point y = `bottom − v / max × (bottom − top)`. Points from `FirstVisible` on; data is clipped to the interior.
  - Horizontal grid lines at `y = ceil(top + k × (bottom − top) / 10)` for k = 1..9. Vertical grid lines at the x of every visible point whose `SequenceAt` is a multiple of `gridSamples`, rounded half away from zero. Grid lines are 1 px, not anti-aliased.
  - Paint order: white interior; grid in `Grid`; main fill polygon (the line, then down to `bottom`) in `Fill`; grid again, clipped to that polygon, in `GridInFill`; for a filled second series, its fill polygon in `SecondFill` and the grid clipped to it in `GridInSecondFill`; the second line (`SecondLine`, 1 px, dashed with `DashStyle.Dash` when `SecondFill` is empty); the main line (`Line`, 1 px); the border (1 px `Border` with the four corner pixels in `TopCorner` / `BottomCorner`).
  - Data lines and fills are anti-aliased (`SmoothingMode.AntiAlias`), as Task Manager's are.

- [ ] **Step 1: Write the failing tests.** Each paints into a 122×76 `Bitmap` (one CPU per-processor graph: `right` 121, `bottom` 75, grid rows at y 8, 15, 23, 30, 38, 45, 53, 60, 68) and reads pixels with `GetPixel(x, y).ToArgb() & 0xFFFFFF`. Helper: a CPU-shaped history (series 0 total, 1 kernel) with 60 points one second apart.

```csharp
static History Steady(float total, float kernel)
{ var h = new History(2); for (var i = 0; i < 60; i++) h.Add((i + 1) * 10_000_000L, [total, kernel]); return h; }

static int[,] Paint(History h, int gridSamples = 10, int second = 1)
{ using var bmp = new Bitmap(122, 76); using var g = Graphics.FromImage(bmp);
  using var p = new GraphPainter(GraphPalettes.Cpu); p.Paint(g, new Rectangle(0, 0, 122, 76), h, 0, 100, gridSamples, second);
  var px = new int[122, 76]; for (var x = 0; x < 122; x++) for (var y = 0; y < 76; y++) px[x, y] = bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF;
  return px; }

[Fact] public void Border_has_lighter_corners()
{ var px = Paint(new History(2));
  Assert.Equal(0x117DBB, px[0, 40]); Assert.Equal(0x117DBB, px[121, 40]);
  Assert.Equal(0x3A94C7, px[0, 0]); Assert.Equal(0x3A94C7, px[121, 0]);
  Assert.Equal(0x1F85BF, px[0, 75]); Assert.Equal(0x1F85BF, px[121, 75]); }

[Fact] public void Empty_graph_is_white_with_horizontal_grid_rows()
{ var px = Paint(new History(2));
  Assert.Equal(0xD9EAF4, px[50, 8]); Assert.Equal(0xD9EAF4, px[50, 68]); Assert.Equal(0xFFFFFF, px[50, 20]); }

[Fact] public void Mini_graph_has_no_grid()
{ Assert.Equal(0xFFFFFF, Paint(new History(2), gridSamples: 0)[50, 8]); }

[Fact] public void Half_load_fills_the_lower_half()
{ var px = Paint(Steady(50, 0), second: -1);
  Assert.Equal(0xF1F6FA, px[50, 70]); Assert.Equal(0xFFFFFF, px[50, 20]); Assert.Equal(0xCEE2F0, px[50, 45]); }

[Fact] public void Kernel_time_fills_darker_below_its_line()
{ var px = Paint(Steady(80, 30)); // total line at y 15, kernel line at y 52.5
  Assert.Equal(0xD9E7F1, px[50, 65]); Assert.Equal(0xF1F6FA, px[50, 35]);
  Assert.Equal(0xBAD6E8, px[50, 68]); Assert.Equal(0xCEE2F0, px[50, 45]); Assert.Equal(0xD9EAF4, px[50, 8]); }

[Fact] public void Vertical_grid_lines_sit_on_every_tenth_point()
{ var px = Paint(Steady(0, 0)); // sequences 0..59, newest at x 121, dx = 121/59
  Assert.Equal(0xD9EAF4, px[62, 5]); Assert.Equal(0xD9EAF4, px[82, 5]); Assert.Equal(0xFFFFFF, px[60, 5]); }

[Fact] public void Grid_scrolls_with_the_data()
{ var h = Steady(0, 0); h.Add(61 * 10_000_000L, [0f, 0f]); // one more point: every line moves one dx left
  var px = Paint(h);
  Assert.Equal(0xD9EAF4, px[59, 5]); Assert.Equal(0xD9EAF4, px[80, 5]); Assert.Equal(0xFFFFFF, px[62, 5]); }

[Fact] public void Painting_creates_no_gdi_or_user_objects()
{ var h = Steady(70, 20); using var bmp = new Bitmap(122, 76); using var g = Graphics.FromImage(bmp);
  using var p = new GraphPainter(GraphPalettes.Cpu); p.Paint(g, new Rectangle(0, 0, 122, 76), h, 0, 100, 10, 1);
  var gdi = GuiResources(0); var user = GuiResources(1);
  for (var i = 0; i < 1000; i++) p.Paint(g, new Rectangle(0, 0, 122, 76), h, 0, 100, 10, 1);
  Assert.Equal(gdi, GuiResources(0)); Assert.Equal(user, GuiResources(1)); }
```
  `GuiResources(flags)` is `GetGuiResources(GetCurrentProcess(), flags)` through the existing `User32` and `Kernel32` imports.
- [ ] **Step 2: Run them.** Run `dotnet test --filter GraphPainterTests`. Expect FAIL: types not defined.
- [ ] **Step 3: Implement** the palettes and the painter. Clip the polygons with one reused `GraphicsPath` and `Graphics.SetClip(path, CombineMode.Intersect)` inside a `Save`/`Restore` pair.
- [ ] **Step 4: Run them.** Expect PASS (8 tests). If an anti-aliased edge lands on a checked pixel, move the probe pixel one step away from the line, never loosen the color.
- [ ] **Step 5: Commit** with message `feat: graph painter with Task Manager's palettes, scrolling grid and kernel fill`.

---

### Task 5: CPU data

Read the "CPU" section of the data-sources reference first.

**Files:**
- Create: `src/Tmcs/Native/Pdh.cs`, `src/Tmcs/Devices/Cpu/ICpuSource.cs` (with `CpuLoad`, `CpuInfo`), `src/Tmcs/Devices/Cpu/PdhCpuSource.cs`, `src/Tmcs/Devices/Cpu/CpuDevice.cs`
- Modify: `src/Tmcs/Native/Kernel32.cs` (`GetLogicalProcessorInformationEx`, `IsProcessorFeaturePresent`), `src/Tmcs/Native/NtDll.cs` (`SystemTimeOfDayInformation = 3`)
- Test: `tests/Tmcs.Tests/Devices/CpuDeviceTests.cs`, `tests/Tmcs.Tests/Devices/FakeCpuSource.cs`

**Interfaces:**
- Consumes: `Device`, `History` (Task 2); `RawProcess` (M1).
- Produces:
  - `readonly record struct CpuLoad(float Utility, float Privileged)`, both 0–100.
  - `sealed record CpuInfo(string Name, double BaseMhz, int Sockets, int Cores, int LogicalProcessors, string Virtualization, long L1Bytes, long L2Bytes, long L3Bytes)`.
  - `interface ICpuSource : IDisposable { CpuInfo Info { get; } bool Read(Span<CpuLoad> perProcessor, out double totalUtility, out double performancePercent); TimeSpan UpTime(); }`. `Read` fills one entry per logical processor, in processor order; false means the read failed and nothing was written.
  - `sealed class PdhCpuSource : ICpuSource`, the real one:
    - One PDH query for its lifetime with three English counters (`PdhAddEnglishCounterW`): `\Processor Information(*)\% Processor Utility`, `\Processor Information(*)\% Privileged Utility` and `\Processor Information(_Total)\% Processor Performance`. Each `Read` collects once and reads the two arrays with `PdhGetFormattedCounterArrayW(PDH_FMT_DOUBLE | PDH_FMT_NOCAP100)` into buffers that grow on `PDH_MORE_DATA` and never shrink. Values are clamped to 0–100.
    - Instances: parse `group,number` from each name (`_Total` is the machine total; skip `g,_Total`) and map them to processor order (group, then number) without allocating: the names are fixed, so build the name → index map at the first read and compare characters after that. Never trust the array order (PDH sorts names as strings: `0,0 0,1 0,10 …`).
    - The first read has no rate yet and returns zeros.
    - `Info`, read once in the constructor:
      - `Name`: registry `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0\ProcessorNameString`, kept as is. It has a trailing space on this machine, which Task Manager shows too.
      - `BaseMhz`: the `~MHz` value under the same key.
      - Sockets, cores, logical processors and caches: `GetLogicalProcessorInformationEx(RelationAll)`. Count the `RelationProcessorPackage` and `RelationProcessorCore` entries; logical processors are the popcount sum of the core group masks. L1 is the sum of every level-1 cache, data and instruction; L2 and L3 sum their levels.
      - `Virtualization`: `IsProcessorFeaturePresent(PF_VIRT_FIRMWARE_ENABLED = 21)` → `Enabled`, else `Disabled`. Task Manager's "Not capable" needs CPUID and is left out (my call).
    - `UpTime()`: `NtQuerySystemInformation(SystemTimeOfDayInformation)`: `CurrentTime` (offset 8) − `BootTime` (offset 0), both 100 ns. The capture couldn't tell this from `GetTickCount64`; this is my call.
  - `sealed class CpuDevice(ICpuSource source) : Device("cpu", DeviceKind.Cpu, 1 + 2 * source.Info.LogicalProcessors)`:
    - `Title` and `PaneTitle` `CPU`; `Subtitle` = `Info.Name`; `CpuInfo Info`.
    - History series: 0 = total utility; `1 + 2i` = processor i's utility; `2 + 2i` = its privileged (kernel) utility.
    - Latest values: `double Utilization`, `double SpeedMhz` (= `performancePercent × Info.BaseMhz / 100`), `int Processes`, `int Threads`, `int Handles`, `TimeSpan UpTime`.
    - `Sample`: counts come from the snapshot. Processes is the entry count, System Idle Process included (the reference found every system source counts it). Threads and Handles are the sums. A failed `Read` appends no point and leaves the latest values as they were.

- [ ] **Step 1: Write the failing tests.** `FakeCpuSource` returns fixed `CpuInfo` (4 processors, base 4500 MHz) and the loads, total and performance a test sets.
  - `Sample_appends_total_and_per_processor_series`: loads `[(10,1), (20,2), (30,3), (40,4)]`, total 25, performance 98 → `History.Count == 1`, `ValueAt(0, 0) == 25`, `ValueAt(1 + 2 * 3, 0) == 40`, `ValueAt(2 + 2 * 3, 0) == 4`, `Utilization == 25`, `SpeedMhz == 4410`.
  - `Counts_come_from_the_snapshot`: three `RawProcess` entries with threads 2, 3, 4 and handles 10, 20, 30 → `Processes == 3`, `Threads == 9`, `Handles == 60`.
  - `Failed_read_appends_nothing`: the fake returns false → `History.Count == 0`.
  - `Title_and_subtitle`: `Title == "CPU"`, `Subtitle == Info.Name`, `Key == "cpu"`.
  - `Real_source_reads_every_logical_processor` (live): `Info.LogicalProcessors == CpuMath.MachineLogicalProcessors()`; read, sleep 1100 ms, read again → true, every utility and privileged value in [0, 100], `Privileged <= Utility + 1` for every processor, total in [0, 100], performance > 0.
  - `Real_static_info_is_sane` (live): `Sockets >= 1`, `Cores >= Sockets`, `LogicalProcessors >= Cores`, `L1Bytes > 0`, `BaseMhz > 0`, `Name` not blank, `UpTime() > TimeSpan.Zero`.
  - `Real_read_does_not_allocate` (live): after two warm-up reads, 100 reads change `GC.GetAllocatedBytesForCurrentThread()` by 0.
- [ ] **Step 2: Run them.** Run `dotnet test --filter CpuDeviceTests`. Expect FAIL: types not defined.
- [ ] **Step 3: Implement** `Pdh`, the native additions, `PdhCpuSource` and `CpuDevice`. `PDH_FMT_COUNTERVALUE_ITEM_W` is 24 bytes on x64: `szName` pointer at 0, `CStatus` at 8, the double at 16. Skip items whose `CStatus` isn't `PDH_CSTATUS_VALID_DATA` (0) or `PDH_CSTATUS_NEW_DATA` (1).
- [ ] **Step 4: Run them.** Expect PASS (7 tests).
- [ ] **Step 5: Commit** with message `feat: CPU data from Processor Information utility counters`.

---

### Task 6: Memory data

Read the "Memory" section of the data-sources reference first.

**Files:**
- Create: `src/Tmcs/Devices/Memory/IMemorySource.cs` (with `MemoryCounters`, `MemoryInfo`), `src/Tmcs/Devices/Memory/SystemMemorySource.cs`, `src/Tmcs/Devices/Memory/Smbios.cs`, `src/Tmcs/Devices/Memory/MemoryDevice.cs`
- Modify: `src/Tmcs/Processes/RawProcess.cs`, `src/Tmcs/Processes/ProcessSnapshotParser.cs` (the working set), `src/Tmcs/Native/NtDll.cs`, `src/Tmcs/Native/Kernel32.cs`
- Test: `tests/Tmcs.Tests/Devices/MemoryDeviceTests.cs`, `tests/Tmcs.Tests/Processes/ProcessSnapshotParserTests.cs`

**Interfaces:**
- Consumes: `Device` (Task 2); `PerfFormat` (Task 3); `RawProcess` (M1).
- Produces:
  - `RawProcess` gains a last positional parameter `long WorkingSet = 0`, parsed from `WorkingSetSize` at offset 0x90 of the process entry. The default keeps every existing constructor call compiling.
  - `readonly record struct MemoryCounters(long Available, long Committed, long CommitLimit, long PagedPool, long NonPagedPool, long Modified, long Standby, long Free)`, all bytes. `Free` includes the zeroed list.
  - `sealed record MemoryInfo(long Installed, long TotalPhysical, int SpeedMhz, int SlotsUsed, int Slots, string FormFactor)`, with `long HardwareReserved => Installed − TotalPhysical`.
  - `interface IMemorySource { MemoryInfo Info { get; } bool Read(out MemoryCounters counters); }`.
  - `sealed class SystemMemorySource : IMemorySource`, the real one. Per read, two fixed-size `NtQuerySystemInformation` calls into buffers allocated once (never `GetPerformanceInfo`, which costs 12.6 ms because it takes its own process snapshot):
    - `SystemPerformanceInformation` (2), page counts as `uint`: `AvailablePages` @44, `CommittedPages` @48, `CommitLimit` @52, `PagedPoolPages` @112, `NonPagedPoolPages` @116. Multiply by `Environment.SystemPageSize`.
    - `SystemMemoryListInformation` (80), 176 bytes of `nuint` page counts: Zero @0, Free @8, Modified @16, ModifiedNoWrite @24, then `PageCountByPriority[8]` @40. Standby is the sum of the eight; Modified includes ModifiedNoWrite; Free includes Zero.
    - `Info`, read once: `Installed` from `GetPhysicallyInstalledSystemMemory` (KB); `TotalPhysical` from `GlobalMemoryStatusEx.ullTotalPhys`; speed, slots and form factor from SMBIOS (`Smbios`).
  - `static class Smbios { static (int SpeedMhz, int SlotsUsed, int Slots, string FormFactor) ReadMemory(); }`: `GetSystemFirmwareTable('RSMB', 0)`; the table starts 8 bytes in. Walk the structures (formatted part of `length` byte 1, then strings up to a double zero). Type 16: `NumberOfMemoryDevices` u16 @0x0D, summed → `Slots`. Type 17: `Size` u16 @0x0C, where 0 means an empty slot → `SlotsUsed` counts the rest; `FormFactor` byte @0x0E of the first populated device (0x09 → `DIMM`, 0x0D → `SODIMM`, otherwise blank); `ConfiguredMemorySpeed` u16 @0x20 when the structure is at least 0x22 long, else `Speed` u16 @0x15 → `SpeedMhz` (max over populated devices). Any failure gives zeros and blank.
  - `sealed class MemoryDevice(IMemorySource source) : Device("memory", DeviceKind.Memory, 1)`:
    - `Title` `Memory`; `Subtitle` = `PerfFormat.Size(Info.Installed)` (`64.0 GB`).
    - Latest values: `long InUse` = TotalPhysical − (Standby + Free + Modified); `long Compressed` = the working set of the snapshot entry named `Memory Compression` whose parent is PID 4 (0 if none); `Available`, `Committed`, `CommitLimit`, `Cached` (= Standby + Modified), `PagedPool`, `NonPagedPool`, `Modified`, `Standby`, `Free`; `MemoryInfo Info`.
    - `string SidebarText`: `Pair(used, TotalPhysical, "GB", 2^30)` + ` (` + `Percent(used / TotalPhysical × 100)` + `)`, where used = TotalPhysical − Available (`38.4/63.7 GB (60%)`). The reference inferred this from one capture; it differs from In use by the Modified list.
    - History series 0 = `InUse` in bytes. A failed read appends nothing.

- [ ] **Step 1: Write the failing tests.**
  - `ProcessSnapshotParserTests.Parse_reads_the_working_set`: write 0x9000 at offset 0x90 of the second entry in the existing synthetic buffer → `list[1].WorkingSet == 0x9000`.
  - `MemoryDeviceTests` with a fake source whose values each test sets (by default TotalPhysical 64 GiB and Installed 64 GiB + 356.5 MiB):
    - `In_use_is_total_minus_standby_free_and_modified`: Standby 22 GiB, Free 3 GiB, Modified 1 GiB → `InUse == 38 GiB`, `Cached == 23 GiB`, `ValueAt(0, 0) == 38 GiB` (as float).
    - `Compressed_is_the_memory_compression_working_set`: a snapshot with `Memory Compression` (parent 4, working set 3 GiB) and another process of the same name with parent 1234 → `Compressed == 3 GiB`.
    - `Sidebar_text_uses_total_minus_available`: TotalPhysical 63.7 GiB, Available 25.3 GiB → `SidebarText == "38.4/63.7 GB (60%)"` (en-US).
    - `Hardware_reserved_is_installed_minus_total`: `Info.HardwareReserved == 356.5 MiB`; `Subtitle == PerfFormat.Size(Info.Installed)`.
    - `Failed_read_appends_nothing`.
    - `Real_lists_add_up_to_total_physical` (live): InUse + Modified + Standby + Free equals TotalPhysical by construction, so check the parts instead: Standby + Free is within 2% of `GlobalMemoryStatusEx.ullAvailPhys`, and every part is > 0.
    - `Real_counters_match_GetPerformanceInfo` (live): Committed, CommitLimit, PagedPool and NonPagedPool are each within 2% of `GetPerformanceInfo`'s values (the test may pay its 12.6 ms).
    - `Real_smbios_reads_the_memory_slots` (live): `Slots >= SlotsUsed >= 1`, `SpeedMhz > 0`.
- [ ] **Step 2: Run them.** Run `dotnet test --filter "MemoryDeviceTests|ProcessSnapshotParserTests"`. Expect the new tests to FAIL.
- [ ] **Step 3: Implement** the parser field, the source, `Smbios` and `MemoryDevice`.
- [ ] **Step 4: Run them.** Expect PASS. Then run `dotnet test` to check that the `RawProcess` change broke nothing.
- [ ] **Step 5: Commit** with message `feat: memory data from the memory lists, performance info and SMBIOS`.

---

### Task 7: Disk data

Read the "Disk" section of the data-sources reference first. Disks 0 and 1 both show `(E:)` because E: is one dynamic (LDM) volume with an extent on each; that is correct, not a duplicate.

**Files:**
- Create: `src/Tmcs/Native/SetupApi.cs`, `src/Tmcs/Devices/Disk/IDiskSource.cs` (with `DiskInfo`, `DiskCounters`), `src/Tmcs/Devices/Disk/SystemDiskSource.cs`, `src/Tmcs/Devices/Disk/DiskDevice.cs`
- Modify: `src/Tmcs/Native/Kernel32.cs` (`CreateFileW`, `DeviceIoControl`, `FindFirstVolumeW`/`FindNextVolumeW`/`FindVolumeClose`, `GetVolumePathNamesForVolumeNameW`, `GetSystemWindowsDirectoryW`), `src/Tmcs/Native/NtDll.cs` (`SystemPageFileInformation = 18`)
- Test: `tests/Tmcs.Tests/Devices/DiskDeviceTests.cs`

**Interfaces:**
- Consumes: `Device` (Task 2); `PerfFormat` (Task 3).
- Produces:
  - `sealed record DiskInfo(int Number, string Letters, string Model, long Capacity, long Formatted, bool IsSystem, bool HasPageFile, string Type)`. `Letters` is `C:`, or several joined by a space (`D: F:`; the separator is my call), or empty. `Type` is `SSD`, `HDD` or `Removable`.
  - `readonly record struct DiskCounters(long BytesRead, long BytesWritten, long ReadTime, long WriteTime, long IdleTime, uint ReadCount, uint WriteCount, long QueryTime)`, as `DISK_PERFORMANCE` gives them (times in 100 ns).
  - `interface IDiskSource : IDisposable { IReadOnlyList<DiskInfo> Enumerate(); bool Read(int number, out DiskCounters counters); }`. `Enumerate` replaces the source's open handles: it keeps one per listed disk, closes the rest, and is called only at startup and when the disks may have changed (Task 10).
  - `sealed class SystemDiskSource : IDiskSource`, the real one. Nothing in it needs elevation.
    - Listing: present `GUID_DEVINTERFACE_DISK` {53f56307-b6bf-11d0-94f2-00a0c91efb8b} interfaces (`SetupDiGetClassDevsW(DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)`, `SetupDiEnumDeviceInterfaces`, `SetupDiGetDeviceInterfaceDetailW`). Open each path with access 0 and `FILE_SHARE_READ | FILE_SHARE_WRITE`; `IOCTL_STORAGE_GET_DEVICE_NUMBER` (0x2D1080) gives `DeviceNumber` @4. Sort by number. (Probing `\.\PhysicalDriveN` or PDH would wrongly add the containers VHDX, drive 5.)
    - Static info per disk: `Model` = `ProductId` from `IOCTL_STORAGE_QUERY_PROPERTY` (0x2D1400) `StorageDeviceProperty`, trimmed (`STORAGE_DEVICE_DESCRIPTOR`: `RemovableMedia` byte @10, `ProductIdOffset` u32 @16). `Capacity` = `DiskSize` @24 of `IOCTL_DISK_GET_DRIVE_GEOMETRY_EX` (0x700A0); not `IOCTL_DISK_GET_LENGTH_INFO`, which needs read access. `Type`: `RemovableMedia` → `Removable` (my call; the rule is unverified), else `StorageDeviceSeekPenaltyProperty` (7) `IncursSeekPenalty` byte @8: 0 → `SSD`, 1 → `HDD`.
    - Volumes: `FindFirstVolumeW`/`FindNextVolumeW`; open each `\?\Volume{…}` (without the trailing backslash) with access 0; `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS` (0x560000): count u32 @0, then 24-byte extents from @8 (`DiskNumber` u32 @0, `ExtentLength` i64 @16). `Formatted` = the sum of a disk's extent lengths. Letters from `GetVolumePathNamesForVolumeNameW` (`C:\` → `C:`), sorted. `IsSystem`: the disk holds the volume of `GetSystemWindowsDirectoryW`'s drive. `HasPageFile`: a name from `SystemPageFileInformation` (entries: `NextEntryOffset` u32 @0, `PageFileName` UNICODE_STRING @16, i.e. length u16 @16, buffer @24; names look like `\??\C:\pagefile.sys`) sits on one of the disk's letters.
    - `Read`: `IOCTL_DISK_PERFORMANCE` (0x70020) on the kept handle into a buffer allocated once: `BytesRead` @0, `BytesWritten` @8, `ReadTime` @16, `WriteTime` @24, `IdleTime` @32, `ReadCount` u32 @40, `WriteCount` u32 @44, `QueryTime` @56.
  - `sealed class DiskDevice(IDiskSource source, DiskInfo info) : Device($"disk:{info.Number}", DeviceKind.Disk, 3)`:
    - `Title`: `Disk 0 (C:)`, or `Disk 0` with no letters. `Subtitle` = `Info.Model`. `DiskInfo Info`.
    - Latest values from the deltas between this read and the previous one: `double ActivePercent` = clamp(100 × (1 − ΔIdleTime / ΔQueryTime), 0, 100) (not PDH `% Disk Time`); `double ResponseMs` = Δ(ReadTime + WriteTime) / Δ(ReadCount + WriteCount) / 10 000, 0 with no I/O; `double ReadBytesPerSecond`, `double WriteBytesPerSecond` = Δbytes / (ΔQueryTime / 10⁷).
    - The first read, and any read where a 64-bit counter went backwards or `ΔQueryTime <= 0`, sets every rate to 0 and becomes the new baseline: never a spike.
    - `ReadCount` and `WriteCount` are 32-bit and wrap during long uptimes. Their deltas are unchecked `uint` subtractions, so a wrap is an ordinary delta, not a reset (Review Focus 3).
    - History series: 0 = active %, 1 = read B/s, 2 = write B/s.
    - A failed `Read` appends nothing and sets `Failed`, so the model re-enumerates disks on the next tick.

- [ ] **Step 1: Write the failing tests** with a fake source:
  - `Rates_come_from_counter_deltas`: baseline, then a read 1 s later (`QueryTime` +10⁷) with `IdleTime` +2.5×10⁶, `BytesRead` +1 MiB, `BytesWritten` +2 MiB, `ReadTime` +20 000, `WriteTime` +12 000, 4 reads and 4 writes → `ActivePercent == 75`, `ReadBytesPerSecond == 1 MiB`, `WriteBytesPerSecond == 2 MiB`, `ResponseMs == 0.4`, and the second point's series 0 is 75.
  - `First_read_is_zero_not_a_spike`: a first read with large totals → every rate 0, one point of zeros.
  - `Counters_going_backwards_reset_the_baseline`: baseline, then a read with smaller totals → rates 0; a third normal read → normal rates.
  - `Io_counts_wrapping_past_uint_max_keep_working`: `ReadCount` goes from 0xFFFFFFFE to 2 and `WriteCount` from 0xFFFFFFFF to 3, with `ReadTime` +20 000 and `WriteTime` +12 000 → `ResponseMs == 0.4`, and the byte rates are the normal ones, not 0.
  - `Title_with_and_without_letters`: `Disk 4 (C:)`; `Disk 5` with no letters; `Key == "disk:4"`.
  - `Failed_read_marks_the_device_failed`.
  - `Real_listing_has_one_system_disk` (live): numbers distinct and ascending, exactly one `IsSystem`, every `Capacity > 0`, `Formatted <= Capacity`.
  - `Real_reads_work_without_elevation` (live, the test host is not elevated): `Read` succeeds for every listed disk, and `QueryTime` grows between two reads.
- [ ] **Step 2: Run them.** Run `dotnet test --filter DiskDeviceTests`. Expect FAIL.
- [ ] **Step 3: Implement** the native imports, the source and the device. Handles are `SafeFileHandle`s owned by the source.
- [ ] **Step 4: Run them.** Expect PASS (8 tests).
- [ ] **Step 5: Commit** with message `feat: disk data from IOCTL_DISK_PERFORMANCE and the volume extents`.

---

### Task 8: Network data

Read the "Network" section of the data-sources reference first.

**Files:**
- Create: `src/Tmcs/Native/IpHlpApi.cs`, `src/Tmcs/Native/WlanApi.cs`, `src/Tmcs/Devices/Network/INetworkSource.cs` (with `AdapterInfo`, `AdapterCounters`, `WifiInfo`), `src/Tmcs/Devices/Network/SystemNetworkSource.cs`, `src/Tmcs/Devices/Network/NetworkDevice.cs`
- Test: `tests/Tmcs.Tests/Devices/NetworkDeviceTests.cs`

**Interfaces:**
- Consumes: `Device` (Task 2); `PerfFormat` (Task 3).
- Produces:
  - `sealed record AdapterInfo(Guid InterfaceGuid, ulong Luid, string Alias, string Description, string? DnsSuffix, bool IsWifi, string? IPv4, string? IPv6)`.
  - `readonly record struct AdapterCounters(ulong InOctets, ulong OutOctets, bool IsUp)`.
  - `sealed record WifiInfo(string Ssid, string PhyType, int SignalQuality)`; `PhyType` is `802.11a`/`b`/`g`/`n`/`ac`/`ad`/`ax` (dot11PhyType 4, 5, 6, 7, 8, 9, 10), else empty.
  - `interface INetworkSource : IDisposable { IReadOnlyList<AdapterInfo> Enumerate(); bool Read(ulong luid, out AdapterCounters counters); WifiInfo? ReadWifi(Guid interfaceGuid); bool Changed { get; } }`. `Changed` is set by the system's change notifications and cleared by `Enumerate`.
  - `sealed class SystemNetworkSource : INetworkSource`, the real one:
    - Listing: `GetAdaptersAddresses(AF_UNSPEC, 0)`; never `GAA_FLAG_SKIP_DNS_SERVER` (it blanks Tailscale's DNS suffix). Keep entries with `OperStatus == 1` (Up) and `IfType != 24` (loopback), **in GAA's own order**. Never `GetIfTable2` or `NetworkInterface.GetAllNetworkInterfaces()`: they add filter instances and vSwitches. x64 `IP_ADAPTER_ADDRESSES_LH` fields used: `Next` @8, `AdapterName` (the `{GUID}` as ANSI) @16, `FirstUnicastAddress` @24, `DnsSuffix` @56, `Description` @64, `FriendlyName` @72, `IfType` @100, `OperStatus` @104. The LUID comes from `ConvertInterfaceGuidToLuid`. `IsWifi` = `IfType == 71`. IPv4 and IPv6 are the first address of each family in the unicast list (`SOCKET_ADDRESS.lpSockaddr` @16 of each entry, `Next` @8), formatted by `IPAddress.ToString()`, which adds `%scope` to a link-local IPv6.
    - Size the GAA buffer by its first call and keep it; it is called only from `Enumerate`.
    - `Read`: `GetIfEntry2` on one `MIB_IF_ROW2` buffer (1352 bytes, allocated once) with `InterfaceLuid` @0 set: `OperStatus` @1156 (Up = 1), `InOctets` @1208, `OutOctets` @1280.
    - `ReadWifi`: one `WlanOpenHandle(2)` for the source's lifetime; `WlanQueryInterface(wlan_intf_opcode_current_connection = 7)`; in `WLAN_CONNECTION_ATTRIBUTES`: `uSSIDLength` @520, SSID bytes @524 (decoded as UTF-8), `dot11PhyType` @568, `wlanSignalQuality` @576. `WlanFreeMemory` the result. Null when it isn't connected or the call fails.
    - `Changed`: `NotifyIpInterfaceChange` and `NotifyUnicastIpAddressChange`, registered in the constructor and cancelled with `CancelMibChangeNotify2` in `Dispose`. The callbacks run on a thread-pool thread and only set a `volatile` flag: an `[UnmanagedCallersOnly]` static method, with the source reached through a `GCHandle` passed as the context. Starts true, so the first tick enumerates.
  - `sealed class NetworkDevice(INetworkSource source, AdapterInfo info) : Device("net:" + info.InterfaceGuid.ToString("B"), DeviceKind.Network, 2)` (the key's GUID is lowercase with braces, as `AppSettings.HiddenAdapters` stores it):
    - `Title`: `Wi-Fi` when `IsWifi`, else `Ethernet`. `Subtitle` = `Description`. `AdapterInfo Info`.
    - Latest values: `double SendBitsPerSecond`, `double ReceiveBitsPerSecond` = Δoctets × 8 / Δt, with Δt from the sample times; `WifiInfo? Wifi` (re-read each sample for Wi-Fi adapters); `string ConnectionType` = `Wifi.PhyType` for Wi-Fi, else `Ethernet`.
    - The first read and counters going backwards give 0 and a new baseline, as for disks.
    - History series: 0 = receive bits/s (the filled, solid series), 1 = send bits/s (the dashed one).
    - A failed `Read`, or `IsUp` false, appends nothing and sets `Failed`.
  - `static int SignalBars(int quality)`: 0 for 0, else `Math.Min(5, quality / 20 + 1)`. The reference couldn't find Task Manager's mapping; this one gives the captured 4 of 5 bars at quality 60–79 and is my call.

- [ ] **Step 1: Write the failing tests** with a fake source:
  - `Rates_are_bits_per_second_from_octet_deltas`: baseline, then 1 s later `InOctets` +5000 and `OutOctets` +1000 → `ReceiveBitsPerSecond == 40_000`, `SendBitsPerSecond == 8_000`; series 0 is 40 000 and series 1 is 8 000.
  - `First_read_and_backwards_counters_give_zero`.
  - `Wifi_title_connection_type_and_key`: an adapter with `IsWifi` and `WifiInfo("frame", "802.11ax", 60)` → `Title == "Wi-Fi"`, `ConnectionType == "802.11ax"`; a wired one → `Ethernet` and `Ethernet`; `Key == "net:{3f2504e0-4f89-11d3-9a0c-0305e82c3301}"` for that GUID.
  - `Down_adapter_marks_the_device_failed`: `IsUp` false → `Failed`, no point.
  - `Signal_bars`: 0 → 0, 1 → 1, 60 → 4, 79 → 4, 80 → 5, 100 → 5.
  - `Real_listing_matches_up_non_loopback_adapters` (live): every listed adapter has a non-empty alias and description and a nonzero LUID; no description contains `Loopback`; `Read` succeeds with `IsUp` for each.
  - `Real_change_flag_starts_set_and_enumerate_clears_it` (live).
- [ ] **Step 2: Run them.** Run `dotnet test --filter NetworkDeviceTests`. Expect FAIL.
- [ ] **Step 3: Implement** the native imports, the source and the device.
- [ ] **Step 4: Run them.** Expect PASS (7 tests).
- [ ] **Step 5: Commit** with message `feat: network adapter data in Task Manager's list and order`.

---

### Task 9: GPU data

Read the whole GPU data-sources reference first: its traps decide the design. In short, hardware-accelerated GPU scheduling is on (the owner's setting), so D3DKMT's global node statistics read 0 for the 3D engine. Utilization therefore comes from PDH `\GPU Engine(*)\Running Time`, as Task Manager's does. Everything else comes from D3DKMT, SetupAPI and the registry, read once.

**Files:**
- Create: `src/Tmcs/Native/Gdi32.cs` (the `D3DKMT*` exports), `src/Tmcs/Devices/Gpu/IGpuSource.cs` (with `GpuInfo`, `GpuEngine`, `GpuCounters`), `src/Tmcs/Devices/Gpu/GpuEngineCounters.cs`, `src/Tmcs/Devices/Gpu/SystemGpuSource.cs`, `src/Tmcs/Devices/Gpu/GpuDevice.cs`
- Modify: `src/Tmcs/Native/Pdh.cs` (`PdhGetRawCounterArrayW`), `src/Tmcs/Native/SetupApi.cs` (`SetupDiEnumDeviceInfo`, `SetupDiGetDevicePropertyW`)
- Test: `tests/Tmcs.Tests/Devices/GpuDeviceTests.cs`, `tests/Tmcs.Tests/Devices/GpuEngineCountersTests.cs`

**Interfaces:**
- Consumes: `Device` (Task 2); `PerfFormat` (Task 3); the PDH imports (Task 5); the SetupAPI imports (Task 7).
- Produces:
  - `enum GpuEngine { Other, ThreeD, Copy, VideoEncode, VideoDecode }`, mapped from `DXGK_ENGINE_TYPE`: 3D = 1, VIDEO_DECODE = 2, VIDEO_ENCODE = 3, COPY = 6, and everything else `Other`.
  - `sealed record GpuInfo(int Index, long Luid, string Name, GpuEngine[] NodeEngines, long DedicatedTotal, long SharedTotal, long HardwareReserved, string DriverVersion, string DriverDate, string DirectX, string Location)`. `NodeEngines[n]` is node n's engine. A value that can't be read is a blank string.
  - `readonly record struct GpuCounters(float Utilization, float ThreeD, float Copy, float VideoEncode, float VideoDecode, long DedicatedUsed, long SharedUsed, float? TemperatureC)`.
  - `interface IGpuSource : IDisposable { IReadOnlyList<GpuInfo> Enumerate(); bool ReadAll(Span<GpuCounters> perGpu); int TrackedInstances { get; } }`.
    - `ReadAll` fills one entry per enumerated GPU, in `Enumerate`'s order, from one PDH collect.
    - `TrackedInstances` is `GpuEngineCounters.TrackedInstances`. The diagnostics log records it (Task 19), to show the PDH map stays bounded under GPU process churn.
  - `sealed class GpuEngineCounters` is the PDH arithmetic, kept apart so it can be tested without a GPU:
    - Per raw array item, `void Accumulate(ReadOnlySpan<char> instanceName, long runningTime, long timeStamp)`; after the last item, `void EndSample()`. `float NodePercent(long luid, int node)` reads the last completed sample. `int TrackedInstances` is for tests.
    - Instance names look like `pid_1234_luid_0x00000000_0x0001D8C7_phys_0_eng_0_engtype_3D`. Parse the pid, the LUID (high part, then low part, in hex) and the engine number from the characters, without allocating a string.
    - Per node, % = (Σ ΔRunningTime ÷ ΔtimeStamp) × 100, summed over the instances present in both samples. Both values are in 100 ns units; clamp the result to 0–100. An instance seen for the first time sets its baseline and adds no delta.
    - The previous-sample map is keyed by `(pid, luid, eng)` and rebuilt each sample from the live instances only: two dictionaries, swapped and cleared. Its size follows the live instances, never every instance ever seen.
  - `sealed class SystemGpuSource : IGpuSource`, the real source:
    - **Listing:**
      - Call `D3DKMTEnumAdapters3` with `Filter = 0`.
      - Keep adapters whose `KMTQAITYPE_ADAPTERTYPE` (15) has `RenderSupported` (bit 0) and not `SoftwareDevice` (bit 2).
      - Close every other adapter handle at once. Keep the kept adapters' handles until the next `Enumerate` or `Dispose`.
      - Number the kept adapters 0, 1, … in enumeration order.
      - Never filter by "has nodes" or "has memory": queries on an indirect-display LUID silently return the real GPU's data.
    - **Static values,** per kept adapter:
      - `Name`: `KMTQAITYPE_DRIVER_DESCRIPTION` (65).
      - Nodes: `D3DKMT_QUERYSTATISTICS` type 0 gives `NodeCount`, then `KMTQAITYPE_NODEMETADATA` (25) gives each node's engine type.
      - `SharedTotal`: `KMTQAITYPE_GETSEGMENTSIZE` (3), `SharedSystemMemorySize`.
      - `DedicatedTotal`: the registry value `HardwareInformation.qwMemorySize` (REG_QWORD) in the adapter's class key, found through `KMTQAITYPE_PHYSICALADAPTERPNPKEY` (41) with the software key type (map `\REGISTRY\MACHINE\` to `HKLM`). Not `DedicatedVideoMemorySize`, which shows 23.8 GB instead of 24.0.
      - `HardwareReserved`: qwMemorySize − `DedicatedVideoMemorySize`.
      - `DriverVersion`, `DriverDate` and `Location`: SetupAPI (`GUID_DEVCLASS_DISPLAY`, present devices), joined to the adapter on the PCI address from `KMTQAITYPE_ADAPTERADDRESS` (6). The date is the FILETIME read as UTC, shown with `ToString("d")` in the current culture.
      - `DirectX`: from `HKLM\SOFTWARE\Microsoft\DirectX\{unique GUID}` (the GUID from `KMTQAITYPE_QUERY_ADAPTER_UNIQUE_GUID`, 60).
        - `MaxD3D12FeatureLevel` 0xC100 gives `12 (FL 12.1)`.
        - Without it, `MaxD3D11FeatureLevel` gives `11 (FL x.y)`. Without either, blank.
      - **Never create a D3D device, not even a NULL-device probe** (my call). It maps the driver's DLLs into tmcs for good: the reference measured 2.5 MB kept after a probe, and 72 MB for a real device.
    - **Per tick:**
      - One `PdhCollectQueryData`, then one `PdhGetRawCounterArrayW` of `\GPU Engine(*)\Running Time` into a reused buffer (grow it on `PDH_MORE_DATA`). Feed the items to `GpuEngineCounters`, skipping items whose `CStatus` isn't valid.
      - Then, per GPU, one `D3DKMT_QUERYSTATISTICS` type 3 (segment) call per segment. Sum `BytesResident` by `Aperture`: 0 is dedicated, 1 is shared.
      - And one `KMTQAITYPE_ADAPTERPERFDATA` (62) call: `Temperature` in tenths of °C, or null if the call fails.
    - Engine values: each of the four engine graphs shows the max over the nodes of that engine type, the six Copy nodes included (max is my call). `Utilization` is the max over all nodes.
    - Use the struct layouts in the reference's "Struct layouts (x64, verified)" section verbatim. Zero the 808-byte `D3DKMT_QUERYSTATISTICS` buffer before each call.
  - `sealed class GpuDevice(GpuInfo info) : Device($"gpu:{info.Luid:x}", DeviceKind.Gpu, 7)`:
    - `Title` `GPU 0` (from `Index`); `PaneTitle` `GPU`; `Subtitle` = `Name`. `GpuInfo Info`.
    - `void Accept(in GpuCounters counters, long time100Ns)` stores `GpuCounters Counters` and appends the point. The model calls `ReadAll` once per tick and hands each GPU its entry (Task 10), so `Sample` does nothing.
    - History series: 0 = utilization, 1 = 3D, 2 = Copy, 3 = Video Encode, 4 = Video Decode, 5 = dedicated used bytes, 6 = shared used bytes.
    - `string? TemperatureText`: `"(" + PerfFormat.Celsius(t) + ")"`, or null without a temperature. The sidebar draws it 5 px after the utilization percent (Task 12).

- [ ] **Step 1: Write the failing tests.**
  - `GpuEngineCountersTests` (no GPU needed):
    - `Node_percent_is_running_time_delta_over_time_delta`: `pid_10_luid_0x00000000_0x0001D8C7_phys_0_eng_0_engtype_3D` reads 1 000 000 at time 0, then 3 000 000 at time 10 000 000. Expect `NodePercent(0x1D8C7, 0) == 20`.
    - `Instances_are_summed_per_node`: two pids on eng 0, each +1 000 000 over 10 000 000 → 20.
    - `Other_luids_are_ignored`.
    - `A_new_instance_counts_from_its_second_sample`: an instance first seen in sample 2 adds nothing.
    - `Exited_instances_are_forgotten`: run 1000 samples, each with 500 fresh pids. Expect `TrackedInstances == 500`.
    - `Parsing_does_not_allocate`: after warm-up, accumulating 500 names allocates 0 bytes.
  - `GpuDeviceTests`:
    - `Accept_appends_all_seven_series`.
    - `Title_pane_title_and_key`: `GPU 0`, `GPU`, `gpu:1d8c7`.
    - `Temperature_text_is_rounded_and_optional`: 37.8 → `(38 °C)`; null → null.
    - `Real_listing_has_only_render_hardware_adapters` (live): no listed name contains `Basic Render`, `DisplayLink` or `Parsec`, and every `DedicatedTotal > 0`.
    - `Real_read_all_returns_sane_values` (live): two reads 1 s apart give every percentage in [0, 100] and `DedicatedUsed > 0`. Return early with an output message when no GPU is listed.
- [ ] **Step 2: Run them.** Run `dotnet test --filter "GpuDeviceTests|GpuEngineCountersTests"`. Expect FAIL.
- [ ] **Step 3: Implement** the imports, `GpuEngineCounters`, the source and the device.
- [ ] **Step 4: Run them.** Expect PASS (11 tests).
- [ ] **Step 5: Commit** with message `feat: GPU data from PDH engine running time and D3DKMT`.

---

### Task 10: Performance model on the sample clock

**Files:**
- Create: `src/Tmcs/Performance/DeviceSources.cs`, `src/Tmcs/Performance/PerformanceModel.cs`
- Modify: `src/Tmcs/AppServices.cs`, `src/Tmcs/MainForm.cs`, `src/Tmcs/Native/User32.cs` (`WM_DEVICECHANGE` constants), `src/Tmcs/Devices/Disk/DiskDevice.cs`, `src/Tmcs/Devices/Network/NetworkDevice.cs`, `src/Tmcs/Devices/Gpu/GpuDevice.cs`
- Create: `tests/Tmcs.Tests/Support/FakeDeviceSources.cs`. Move the fake sources of Tasks 6–9 out of their test classes into `tests/Tmcs.Tests/Devices/Fake*Source.cs`, one file each, as Task 5's `FakeCpuSource` already is.
- Modify: every `new AppServices(...)` in the tests (`MainFormTests.cs`, `MainFormTests.TestMode.cs`)
- Test: `tests/Tmcs.Tests/Performance/PerformanceModelTests.cs`, `tests/Tmcs.Tests/MainFormTests.cs`

**Interfaces:**
- Consumes: the devices and sources of Tasks 5–9; `RawProcess`, `SampleClock` (M1).
- Produces:
  - `sealed class DeviceSources(ICpuSource cpu, IMemorySource memory, IDiskSource disks, INetworkSource network, IGpuSource gpu) : IDisposable`, with properties `Cpu`, `Memory`, `Disks`, `Network`, `Gpu`. `Dispose` disposes the four disposable sources. `static DeviceSources CreateReal()` builds `PdhCpuSource`, `SystemMemorySource`, `SystemDiskSource`, `SystemNetworkSource` and `SystemGpuSource`.
  - A rule for every real source: it never throws on the tick path, and never from its constructor because something is missing or broken (PDH counters disabled or corrupt, no GPU, the WLAN service stopped). It returns false or an empty list instead and writes the reason once with `Debug.WriteLine`. tmcs must open whatever the machine lacks.
  - `AppServices` gains a last positional parameter, `DeviceSources Devices`. `CreateReal` passes `DeviceSources.CreateReal()`. The form owns it and disposes it through the model.
  - `sealed class PerformanceModel(DeviceSources sources) : IDisposable`, in `Tmcs.Performance`:
    - `IReadOnlyList<Device> Devices`, in sidebar order: the `CpuDevice`, the `MemoryDevice`, the disks in `IDiskSource.Enumerate` order, the adapters in `INetworkSource.Enumerate` order, then the GPUs in `IGpuSource.Enumerate` order. The constructor enumerates each kind once.
    - `CpuDevice Cpu`, `MemoryDevice Memory`.
    - `event Action? DevicesChanged`: raised inside `Sample` when a rescan added, removed or reordered devices, before any device samples.
    - `int GpuInstances => sources.Gpu.TrackedInstances`, for the diagnostics log (Task 19).
    - `void MarkDevicesChanged()`: only sets flags, so the next `Sample` re-enumerates disks and GPUs.
    - `void Sample(long time100Ns, IReadOnlyList<RawProcess> snapshot)`, in this order:
      1. **Rescans.**
         - Disks: when marked, or when a disk failed in the last sample.
         - Adapters: when `sources.Network.Changed` is set, or when an adapter failed in the last sample.
         - GPUs: when marked, or when the last `ReadAll` failed.
         - A rescan keeps the device object, and so its history and rate baseline, for every key still listed, and passes it the new info through `Update(info)`. It creates devices for new keys, drops devices whose key is gone, and rebuilds the list into a reused `List<Device>`.
         - Keeping the objects matters: `NotifyUnicastIpAddressChange` fires on every DHCP renewal, and recreating the device would wipe its graph.
      2. `Cpu.Sample`, `Memory.Sample`, then each disk's and each adapter's `Sample`.
      3. One `sources.Gpu.ReadAll` into a reused `GpuCounters[]`, which is resized only when the GPU count changes. Then each `GpuDevice.Accept(counters[i], time100Ns)`. A false result appends nothing and marks a GPU rescan.
      4. A disk or adapter whose `Failed` is set marks its kind for a rescan in the next sample.
    - Hidden adapters are sampled like the others, so "Show hidden adapters" shows real graphs. Only the sidebar filters them out.
    - Nothing runs on its own. Only `MainForm`'s sample calls the model, so Paused reads nothing, and a rescan happens only at the start of a sample.
    - With no device change, `Sample` allocates nothing.
  - `DiskDevice.Update(DiskInfo)`, `NetworkDevice.Update(AdapterInfo)` and `GpuDevice.Update(GpuInfo)` replace `Info`, which becomes `{ get; private set; }`. The history and the rate baseline are kept.
  - `MainForm`:
    - `internal PerformanceModel Performance`, created from `services.Devices` and disposed with the form.
    - `OnSample` calls `Performance.Sample(t, _snapshot)` right after `_table.Apply(_snapshot, t)`, with the same `t`. A failed snapshot read still skips the whole tick, devices included: one missing point, never a half-done tick (my call).
    - `WndProc`: `WM_DEVICECHANGE` (0x0219) with `wParam` `DBT_DEVNODES_CHANGED` (0x0007), `DBT_DEVICEARRIVAL` (0x8000) or `DBT_DEVICEREMOVECOMPLETE` (0x8004) calls `Performance.MarkDevicesChanged()`. Top-level windows get these broadcasts without registering.

- [ ] **Step 1: Write the failing tests.** `FakeDeviceSources.Create()` gives 2 disks (numbers 0 and 4), 2 adapters (GUIDs A and B), and 1 GPU with LUID 0x1D8C7. Each fake counts its `Enumerate` and `Read` calls, and lets a test change what it lists and make reads fail.
  - `PerformanceModelTests`:
    - `Devices_are_in_sidebar_order`: the keys are `cpu`, `memory`, `disk:0`, `disk:4`, `net:{A}`, `net:{B}`, `gpu:1d8c7`.
    - `Sample_appends_one_point_to_every_device`: 3 samples 1 s apart → every `History.Count == 3`.
    - `A_failed_disk_drops_out_on_the_next_sample`:
      - Disk 4's read fails and the fake stops listing it.
      - After sample 2, disk 4 is still listed and `Failed`.
      - After sample 3 it is gone, `DevicesChanged` has been raised once, and `disk:0` is the same object with `History.Count == 3`.
    - `Adapter_change_keeps_the_history_and_updates_the_info` (Review Focus 1):
      - `Changed` is set, and the new list is B with a new IPv4, then C.
      - B is the same object, with the new `Info.IPv4` and its history.
      - A is dropped, and C comes after B.
    - `Marked_changes_rescan_on_the_next_sample_only`:
      - After `MarkDevicesChanged()`, the disk and GPU `Enumerate` counts don't change until `Sample`, then rise by 1 each.
      - A second sample adds none.
    - `Gpu_luid_change_replaces_the_gpu` (Review Focus 2): `ReadAll` fails and the fake now lists the GPU under LUID 0x2000. After the next sample the GPU's key is `gpu:2000`.
    - `Failing_sources_never_throw`: every read fails and no GPU is listed. 100 samples run, the keys are `cpu`, `memory`, then the disks and adapters, and every history stays empty.
    - `Steady_sampling_does_not_allocate`: after 3 warm-up samples, 100 samples change `GC.GetAllocatedBytesForCurrentThread()` by 0.
  - `MainFormTests`:
    - `Every_sample_feeds_the_model_on_any_tab_and_minimized`: fire the timer once on each tab and once minimized. `Performance.Cpu.History.Count` grows by one per fire.
    - `Paused_reads_no_devices_and_refresh_now_reads_once`: set Paused; the fake CPU's read count doesn't change. F5 adds exactly one read.
    - `Device_change_message_rescans_on_the_next_sample`: send `WM_DEVICECHANGE` with `DBT_DEVNODES_CHANGED`. The disk `Enumerate` count doesn't change until the timer fires, then rises by 1.
- [ ] **Step 2: Run them.** Run `dotnet test --filter "PerformanceModelTests|MainFormTests"`. Expect the new tests to FAIL.
- [ ] **Step 3: Implement** `DeviceSources`, `PerformanceModel`, the `Update` methods and the form wiring. Then update every `new AppServices(...)` in the tests to pass `FakeDeviceSources.Create()`.
- [ ] **Step 4: Run the whole suite.** Run `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: performance model sampling every device on the sample clock`.

---

### Task 11: Performance tab, view shell and the panes' common layout

Read sections 1, 5 and 7 of the look reference first.

**Window-size rule.**
- All geometry is in 96-DPI units, scaled by `DeviceDpi / 96`.
- At Task Manager's size (the view 1278×623, the pane 1052×575) every rectangle must match the captures exactly. The tests build the view and the panes at those sizes, whatever size tmcs's own tab page is.
- At other sizes the layout flexes as section 7 of the look reference describes. Each graph keeps its grid step `s` (CPU 10, GPU engines 4, the rest 3; my call, since the captures have one window size).

**Files:**
- Create in `src/Tmcs/Performance/Views/`: `PaneFonts.cs`, `PaneText.cs`, `InfoLayout.cs`, `PerformancePane.cs` (with `TitlePane`), `PerformanceView.cs`
- Modify: `src/Tmcs/MainForm.cs`, `src/Tmcs/Performance/Graphs/GraphPainter.cs` (tiny bounds)
- Test: `tests/Tmcs.Tests/Performance/InfoLayoutTests.cs`, `tests/Tmcs.Tests/Performance/PerformanceViewTests.cs`, `tests/Tmcs.Tests/Performance/GraphPainterTests.cs`, `tests/Tmcs.Tests/MainFormTests.cs`

**Interfaces:**
- Consumes: `PerformanceModel` (Task 10), `PerfFormat` (Task 3), `GraphPainter` (Task 4).
- Produces:
  - `sealed class PaneFonts(int dpi) : IDisposable`: Segoe UI Regular, created with `GraphicsUnit.Pixel` at these sizes × `dpi / 96`:
    - `Title` 28, `Subtitle` 16, `Caption` 11, `Label` 12, `Value` 20, `Engine` 13.
    - The sidebar uses `Subtitle` for entry titles and `Label` for sub lines.
  - `static class PaneText`:
    - Colors: `Black` 000000, `Gray` 707070.
    - `int Width(string text, Font font)`.
    - `void Draw(Graphics g, string text, Font font, Point origin, Color color, int maxWidth = int.MaxValue)`. A finite `maxWidth` clips with an end ellipsis.
    - Both go through `TextRenderer` with `NoPadding | NoPrefix | SingleLine | PreserveGraphicsClipping`. That is GDI ClearType, as Task Manager draws. `Width` must give the captured widths (see the test).
  - `InfoLayout`: pure geometry for the info block, so it is tested without a window.
    - Types:
      - `enum InfoMarker { None, Solid, Dashed }`.
      - `readonly record struct InfoItem(string Label, string Value, InfoMarker Marker = InfoMarker.None)`.
      - `readonly record struct StaticRow(string Key, string Value, int Height = 18)`. Height is 19 for the Wi-Fi signal row.
      - `readonly record struct PlacedItem(InfoItem Item, int X, int Y, int TextX, int Width)`.
      - `readonly record struct PlacedRow(StaticRow Row, int KeyX, int ValueX, int Y)`.
    - Factories: `static InfoLayout Rows(IReadOnlyList<IReadOnlyList<InfoItem>> rows, IReadOnlyList<StaticRow> table, Func<string, int> labelWidth, Func<string, int> valueWidth, int minMarkedWidth = 0)`, and `static InfoLayout Columns(...)` with the same parameters.
    - Results: `int Height`, `IReadOnlyList<PlacedItem> Items`, `IReadOnlyList<PlacedRow> Table`. Positions are relative to the block's top left.
    - The rules come from look reference section 7:
      - An item's width is the larger of its label's and its value's width. A marked item's is at least `minMarkedWidth`.
      - **Row flow:** each row starts at x 0. A marked item's text starts 8 px after its x. The next item starts 20 px after the previous item's text ends. Row r's label is at y = 51·r and its value 15 px lower.
      - **Columns (GPU):** a column's width is its widest item, and every item in it gets that width. The next column starts 20 px after it. Item r of a column is at y = 51·r.
      - **Table:** keys at x = dynamic right + 12, values at key x + widest key + 14. Each row's y is the sum of the heights of the rows above it.
      - `Height` = max(the longest row or column count × 51 − 8, the sum of the table's row heights).
  - `abstract class PerformancePane : ScrollableControl`. There is one per `DeviceKind`, reused for every device of that kind.
    - Owner-drawn, `DoubleBuffered`, `ResizeRedraw`, white background.
    - `AutoScroll` is on, with `AutoScrollMinSize = (0, ContentHeight)` scaled. Only the GPU pane's `ContentHeight` (615) is above zero, so it alone ever shows the native scrollbar. That costs no extra window and no GDI objects.
    - Members: `Device? Device`, set by the view; `virtual int ContentHeight => 0`; `PaneFonts Fonts`, the view's.
    - Geometry, in content coordinates:
      - `Left = 19`.
      - `Right = client width − 24`, exclusive. The scrollbar narrows the client, which moves `Right`, as it does in Task Manager.
      - `CaptionY = 53`, `GraphTop = 69`.
      - `InfoBottom = max(client height, ContentHeight) − 20`.
    - Paints the title (`PaneTitle`, Title font, black, at (17, 10)) and the subtitle (`Subtitle` font, black, at y 24, right-aligned to `Right`). Then it calls `protected abstract void PaintBody(Graphics g)`. The scroll offset is applied as a transform.
    - `sealed class TitlePane : PerformancePane` paints nothing more. It stands in for every kind until that kind's pane is built (Tasks 13 and 15–18).
    - Helpers for the subclasses:
      - `void Captions(Graphics g, Rectangle graph, string topLeft, string topRight, bool bottom)`. The top captions sit at `graph.Y − 16`. The bottom ones are `60 seconds` at `(graph.X, graph.Bottom + 2)` and `0`. Right captions are right-aligned to `graph.Right`. All are Caption font, gray.
      - `void PaintInfo(Graphics g, InfoLayout layout, Color solid, Color dashed)`, drawn at `(Left, InfoBottom − layout.Height)`:
        - Labels in Label font, gray; values in Value font, black.
        - Markers are 2 px wide and start 3 px below the label's top. A solid one is 40 px tall. A dashed one is 7 dashes of 3 px on and 3 px off.
        - Table keys in Label font, gray; table values in Label font, black.
      - Height-flex helpers: `ChartBottom = info top − 10`; a graph's top caption sits 16 px above it; the next graph's top caption sits 19 px below the previous graph's bottom captions.
    - When a graph's rectangle comes out narrower or shorter than 3 px, it is skipped. Text is clipped. Nothing throws at any size (Review Focus 5).
  - `sealed class PerformanceView : Control`, docked in the Performance tab page.
    - Constructor: `PerformanceView(PerformanceModel model, AppSettings settings)`.
    - Layout, with the origin at Task Manager's tab content (window (8, 81)):
      - The sidebar slot is (4, 7, 214, height − 55). Task 12's sidebar goes there.
      - A 1-px D9D9D9 divider runs at x 218 from y 17 to height − 53.
      - The pane sits at (226, 0, width − 226, height − 48).
      - The bottom 48 px stay white and empty. Task Manager's "Fewer details" and "Open Resource Monitor" are out of scope, and keeping the band keeps the pane as tall as Task Manager's (my call).
    - Holds one pane per `DeviceKind`, all created at startup. Only the selected device's pane is visible.
    - `string SelectedKey` (starts at `cpu`) and `void Select(string key)`. Task 12 moves selection into the sidebar.
    - `void RefreshVisible()`: invalidates the sidebar and the visible pane, nothing else.
    - `PaneFonts Fonts`. On a DPI change (`RescaleConstantsForDpi`), the view disposes the old fonts, creates new ones and invalidates its children. The work happens in `internal void ApplyDpi(int dpi)`, which the tests call.
  - `GraphPainter.Paint` paints nothing when `bounds` is narrower or shorter than 3 px.
  - `MainForm`:
    - The tabs are `Performance`, then `Details`. tmcs opens on Performance, always; the selected tab is not saved.
    - `PerformanceShowing` = the Performance tab is selected and the window isn't minimized. `OnSample` calls `RefreshVisible()` only then. Switching to the tab and restoring the window call it too.
    - `internal PerformanceView PerformanceView`.

- [ ] **Step 1: Write the failing tests.**
  - `InfoLayoutTests`. The width functions return the captured widths from a dictionary, so these test the rules and not the fonts.
    - `Cpu_rows_flow_like_task_managers`:
      - Rows: Utilization (53), Speed (79); Processes (51), Threads (55), Handles (66); Up time (100). Widest key: 100.
      - Item x: row 1 at 0 and 73; row 2 at 0, 71 and 146; row 3 at 0.
      - Table: key x 224, value x 338. `Height` 145.
    - `Disk_marked_items_indent_their_text`:
      - Rows: Active time (60), Average response time (120); then Read speed (Solid, 60), Write speed (Dashed, 76). Widest key: 65.
      - Row 2: the markers at x 0 and 88, the text at 8 and 96.
      - Table: key x 212, value x 291. `Height` 94.
    - `Wifi_signal_row_adds_a_pixel`: 6 table rows, the last 19 tall, and 2 rows of items → `Height` 109.
    - `Gpu_columns_share_their_widest_width`:
      - Column 1: Utilization, GPU Memory, widest 101. Column 2: three items, widest 127.
      - Column 2 starts at x 121. Table key x 260. `Height` 145.
    - `Network_width_only_grows`: with `minMarkedWidth` 86 and 60-px values, the item width is 86 and the table key x is 106.
  - `GraphPainterTests.Tiny_bounds_paint_nothing_and_do_not_throw`: bounds of 2×76, 122×2, 0×0 and −5×−5. Nothing throws, and the bitmap stays white.
  - `PerformanceViewTests`. They run on an STA thread with the off-screen helpers.
    - `Text_widths_match_the_captures`, with the real fonts at 96 DPI:
      - Label font: `Utilization` 53, `Logical processors:` 100.
      - Value font: `4.42 GHz` 79, `25:17:28:41` 100, `38.0 GB (3.6 GB)` 140.
      - If a different flag set gives a constant offset from these, fix the flags. Never change the expected numbers.
    - `Layout_matches_task_manager_at_96_dpi`:
      - View at 1278×623: the sidebar slot is (4, 7, 214, 568) and the pane is (226, 0, 1052, 575).
      - The view's pixel at (218, 17) is D9D9D9, and at (218, 16) it is white.
    - `Pane_geometry`:
      - A `TitlePane` at 1052×575: `Right == 1028`, `InfoBottom == 555`.
      - The same pane with `ContentHeight` 615: the vertical scrollbar shows, `Right == 1011` and `InfoBottom == 595`.
    - `Dpi_changes_recreate_fonts_and_keep_gdi_flat` (Review Focus 4):
      - `ApplyDpi` alternates 96 and 144 fifty times, painting after each.
      - At 144 the `Title` font is 42 px.
      - The GDI and USER counts after the last round equal those after the first.
    - `Painting_creates_no_gdi_or_user_objects`: paint the view 1000 times; the counts stay the same.
    - `Painting_at_tiny_sizes_does_not_throw` (Review Focus 5):
      - Paint the view, and every pane it holds, at 400×300, 100×50, 1×1 and 0×0.
      - Do it for a device of each kind from `FakeDeviceSources`.
      - It covers the panes Tasks 13 and 15–18 add, as they replace the `TitlePane`s.
  - `MainFormTests`:
    - `Opens_on_the_performance_tab`: the tab texts are `Performance`, `Details`, and `Performance` is selected.
    - `Panes_repaint_only_while_visible`:
      - Count the visible pane's paints through an internal counter.
      - With Details selected, or minimized, 5 samples add no paints.
      - Selecting Performance repaints.
    - Update the M1 tests that assumed Details was the startup tab: they now select Details first.
- [ ] **Step 2: Run them.** Run `dotnet test --filter "InfoLayoutTests|PerformanceViewTests|GraphPainterTests|MainFormTests"`. Expect the new tests to FAIL.
- [ ] **Step 3: Implement** the fonts, the text helpers, `InfoLayout`, the pane base, `TitlePane`, the view, the painter guard and the form wiring.
- [ ] **Step 4: Run the whole suite.** Run `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: performance tab shell, pane layout and info block geometry`.

---

### Task 12: Sidebar

Read section 2 of the look reference first.

**Files:**
- Create: `src/Tmcs/Performance/Views/Sidebar.cs`, `src/Tmcs/Performance/GraphScales.cs`
- Modify: `src/Tmcs/Performance/Views/PerformanceView.cs`, `src/Tmcs/MainForm.cs` (the view's `SettingsChanged` → `ScheduleSave`), `src/Tmcs/Settings/AppSettings.cs`, `src/Tmcs/Ui/CommandIds.cs`, `src/Tmcs/Ui/NativeMenu.cs` (`SetText`), `src/Tmcs/Native/User32.cs` (`ModifyMenuW`)
- Test: `tests/Tmcs.Tests/Performance/SidebarTests.cs`, `tests/Tmcs.Tests/Performance/GraphScalesTests.cs`, `tests/Tmcs.Tests/Settings/SettingsTests.cs`, `tests/Tmcs.Tests/Ui/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `PerformanceModel` (Task 10), `PerformanceView` and `PaneText` (Task 11), `GraphPainter` (Task 4), `PerfFormat` (Task 3).
- Produces:
  - `static class GraphScales`. Neither method allocates; captions are constants.
    - `(float Max, string Caption) Network(float bitsPerSecond)`: the smallest of 100 Kbps, 500 Kbps, 1 Mbps, 10 Mbps, 100 Mbps, 1 Gbps, 10 Gbps and 100 Gbps (decimal units) that is at least the value. The captions are exactly those strings.
    - `(float Max, string Caption) Disk(float bytesPerSecond)`: the same rule over 100 KB/s, 500 KB/s, 1 MB/s, 10 MB/s, 100 MB/s, 250 MB/s, 500 MB/s, 1 GB/s, 10 GB/s and 100 GB/s, with binary units (1 KB = 1024).
    - These are my call; the references couldn't verify the rule. Task Manager's string set also holds 10 Kbps and odd steps such as 450 Kbps, which are left out. The floor reproduces every capture, because idle devices show 100 Kbps and 100 KB/s.
  - `sealed class Sidebar : ScrollableControl`, in the view's sidebar slot. It replaces the view's own `SelectedKey`/`Select`.
    - **Entries:** the model's devices, in order, without hidden adapters unless "show hidden" is on.
      - The list is rebuilt into a reused list on `DevicesChanged` and on every hide or show, whether or not the tab is visible. So it never holds a dropped device.
    - **Geometry** (section 2 of the look reference): entries are stacked from y 0 with no gaps.
      - CPU and Memory entries are 194×60, disk entries 194×66, and network and GPU entries 204×66, clipped by the client edge.
      - `AutoScrollMinSize = (0, sum of the heights)` gives the native scrollbar.
    - **Painting an entry:**
      - The background is white, or CDE8FF over the whole entry when it is selected. No border, no focus rectangle.
      - The mini graph sits at entry + (10, 10) and is 60 × (height − 20). It uses the device's palette and `gridSamples` 0.
      - The title is at (80, 5) in the Subtitle font. Sub line 1 is at (81, 26) and sub line 2 at (81, 41), in the Label font.
      - All text is black unless the table says otherwise, and is clipped at x 195 with an end ellipsis.
      - Only entries that meet the clip rectangle are painted.

      | Kind | Mini graph | Sub line 1 | Sub line 2 |
      |---|---|---|---|
      | CPU | series 0, max 100, palette `Cpu`, no kernel series | `Percent(Utilization)`, then `Ghz(SpeedMhz)` 5 px after it | – |
      | Memory | series 0, max `TotalPhysical` | `SidebarText` | – |
      | Disk | series 0, max 100 | `Info.Type` | `Percent(ActivePercent)` |
      | Network | series 0 solid and series 1 dashed, max `GraphScales.Network(the larger visible max)` | `Info.Alias` | four segments, each starting where the previous one's measured width ends: `S: ` (gray), the send value and a space, `R: ` (gray), the receive value with its unit and a space |
      | GPU | series 0, max 100, palette `Gpu` | `Info.Name` | `Percent(Counters.Utilization)`, then `TemperatureText` 5 px after it. Black: its pixels were never captured (my call). |

    - **Selection:**
      - `string SelectedKey`, which starts at `cpu`, and `event Action? SelectionChanged`. The view shows the selected device's pane.
      - A click selects an entry. Up and Down select the previous and next entry and scroll it into view, as Task Manager's sidebar does (my call).
      - `bool SelectFirst(DeviceKind kind)` selects the first listed entry of that kind and returns false when there is none (Task 19).
      - **When the selected device leaves the list** (removed or hidden), the selection moves on the same rebuild:
        - first to an entry of the same kind with the same `Title` and `Subtitle` (a GPU whose LUID changed after a driver restart, or a re-created adapter);
        - else to the entry that was above it;
        - else to CPU.
    - **Right-click menu:** a `NativeMenu` built once and shown with `Track`.
      - On a network entry: `&Hide`, a separator, then `&Show hidden adapters (N)`.
      - Anywhere else in the sidebar: `&Show hidden adapters (N)` only.
      - `Hide` is checked when that adapter is hidden, which can only be seen while hidden adapters are shown, so the same item un-hides it. It toggles the adapter's GUID in `AppSettings.HiddenAdapters` and raises `event EventHandler? SettingsChanged`. The view forwards that to the form's `ScheduleSave`.
      - `Show hidden adapters` toggles a flag for the session only. The spec says hiding is remembered, not this toggle (my call). The item is checked while hidden adapters are shown, and disabled when N is 0 and they aren't shown.
      - N counts the hidden adapters the model currently lists (my call: GUIDs of absent adapters don't count). The text is set each time the menu opens, through `NativeMenu.SetText(int id, string text)` (`ModifyMenuW` with `MF_BYCOMMAND | MF_STRING`).
      - `CommandIds.HideAdapter = 400` and `CommandIds.ShowHiddenAdapters = 401`. The handling is in `internal void OnCommand(int id, Device? target)`, which tests call directly, since a tracked menu would block.
  - `AppSettings.Normalize` keeps only `HiddenAdapters` entries that parse as GUIDs, rewrites them as `ToString("B")` (lowercase, with braces), and drops duplicates. Update the M1 doc comment "Unused until M2".

- [ ] **Step 1: Write the failing tests.**
  - `GraphScalesTests`:
    - `Network_steps`: 0 → (100 000, `100 Kbps`); 40 000 → `100 Kbps`; 450 000 → (500 000, `500 Kbps`); 323 000 000 → `1 Gbps`; 9 400 000 000 → (10¹⁰, `10 Gbps`).
    - `Disk_steps`: 0 → `100 KB/s`; 1.3 MiB → `10 MB/s`; 708 MiB → `1 GB/s`.
  - `SidebarTests`. They run on an STA thread with a model over fakes: CPU, Memory, 5 disks, 12 adapters and 1 GPU, as on the owner's machine.
    - `Entries_stack_with_task_managers_heights`: the entry tops are 0, 60, 120, 186, …. The GPU's top is 1242, and `AutoScrollMinSize.Height == 1308`.
    - `Hidden_adapters_are_left_out_until_shown`.
    - `Hide_toggles_the_setting_and_raises_settings_changed`: through `OnCommand`. The stored GUID is lowercase with braces.
    - `Hiding_the_selected_adapter_selects_the_entry_above`.
    - `Removed_selected_device_falls_back_on_the_same_sample` (Review Focus 1): the selected adapter B is removed in a rescan. After that sample the selection is the entry above B, and the view shows its pane.
    - `Selection_follows_a_gpu_whose_luid_changed` (Review Focus 2): a rescan replaces `gpu:1d8c7` with `gpu:2000`, with the same title and name. Then `SelectedKey == "gpu:2000"`.
    - `Dropped_devices_are_released_while_the_details_tab_shows`: with the Details tab selected, the model drops an adapter. The sidebar's internal entry list no longer holds it.
    - `Show_hidden_count_and_state`: two hidden GUIDs, one of them listed → the item text is `&Show hidden adapters (1)`. With none hidden, the item is disabled.
    - `Up_and_down_move_the_selection`.
    - `Painting_creates_no_gdi_or_user_objects`: 1000 paints.
  - `SettingsTests.Hidden_adapters_are_normalized`: `{3F2504E0-4F89-11D3-9A0C-0305E82C3301}`, `bogus` and `{3f2504e0-4f89-11d3-9a0c-0305e82c3301}` become one entry, `{3f2504e0-4f89-11d3-9a0c-0305e82c3301}`.
  - `NativeMenuTests.Set_text_changes_the_item_text`: read the text back with `GetMenuStringW`.
- [ ] **Step 2: Run them.** Run `dotnet test --filter "GraphScalesTests|SidebarTests|SettingsTests|NativeMenuTests"`. Expect the new tests to FAIL.
- [ ] **Step 3: Implement** the scales, the sidebar, the menu text change, the settings repair and the view wiring.
- [ ] **Step 4: Run the whole suite.** Run `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: performance sidebar with mini graphs and hideable adapters`.

---

### Task 13: CPU pane

Read the "CPU per-LP grid" part of section 4 and section 7 of the look reference first.

**Files:**
- Create: `src/Tmcs/Performance/Views/CpuPane.cs`
- Modify: `src/Tmcs/Performance/Views/PerformanceView.cs` (the CPU kind gets this pane)
- Test: `tests/Tmcs.Tests/Performance/CpuPaneTests.cs`

**Interfaces:**
- Consumes: `CpuDevice` (Task 5), `PerformancePane` and `InfoLayout` (Task 11), `GraphPainter` (Task 4).
- Produces `sealed class CpuPane : PerformancePane`:
  - **Captions:** `% Utilization over 60 seconds` at (Left + 2, CaptionY), and `100%` right-aligned to Right − 2.
  - **The grid:**
    - It spans x from Left + 2 to Right − 2, and y from GraphTop to ChartBottom − 15.
    - For n logical processors: rows = max(1, round(√(n / 2))), and columns = ⌈n / rows⌉. That gives 8 × 4 for 32; other counts are my call.
    - Gaps are 4 px. A cell's width is ⌊(grid width − 4 × (columns − 1)) / columns⌋, and the last column takes the remainder. Heights work the same way, with the last row.
    - Cells are in row-major order, LP 0 at the top left.
  - **Each cell:** `Paint(g, cell, history, 1 + 2i, 100, 10, second: 2 + 2i)`, with one `GraphPainter(GraphPalettes.Cpu)` that the pane creates once.
  - The 13-px row below the grid stays empty: Task Manager draws no "60 seconds" here.
  - **Info (row flow):**
    - Rows: `Utilization` (`Percent`) and `Speed` (`Ghz(SpeedMhz)`); `Processes`, `Threads` and `Handles` (integers with no group separator); `Up time` (`UpTime`).
    - Table: `Base speed:` (`Ghz(BaseMhz)`), `Sockets:`, `Cores:`, `Logical processors:`, `Virtualization:`, and `L1 cache:`, `L2 cache:`, `L3 cache:` (`Size`).
  - `internal InfoLayout BuildInfo()` and `internal Rectangle CellBounds(int processor)`, for the tests.

- [ ] **Step 1: Write the failing tests.** The pane is 1052×575 at 96 DPI, with a fake CPU of 32 logical processors.
  - `Cells_match_task_manager`: cell 0 is (21, 69, 122, 76), cell 7 is (903, 69, 123, 76), cell 24 is (21, 309, 122, 76) and cell 31 is (903, 309, 123, 76).
  - `Grid_shape_for_other_counts`: 16 → 6 × 3; 8 → 4 × 2; 1 → 1 × 1.
  - `Info_starts_where_task_managers_does`: the info top is 410 (InfoBottom 555 − Height 145).
  - `Info_uses_task_managers_formats`: utilization 25.4, 4410 MHz, 481 processes and 25 d 17:28:41 give the values `25%`, `4.41 GHz`, `481` and `25:17:28:41`. The table holds 8 rows, with `1.0 MB` for the L1 cache.
  - `Each_cell_draws_its_processor_and_kernel_time`: LP 5 at 80% total and 30% kernel; every other LP at 0. A pixel in the lower part of cell 5 is the kernel fill D9E7F1. The same pixel in cell 6 is white.
  - `Painting_creates_no_gdi_or_user_objects`: 1000 paints.
- [ ] **Step 2: Run them.** Run `dotnet test --filter CpuPaneTests`. Expect FAIL.
- [ ] **Step 3: Implement** `CpuPane` and give the view's CPU kind this pane.
- [ ] **Step 4: Run the whole suite.** Run `dotnet test`. Expect all PASS. Task 11's `Painting_at_tiny_sizes_does_not_throw` now covers this pane too.
- [ ] **Step 5: Commit** with message `feat: CPU pane with 32 per-processor graphs and kernel time`.

---

### Task 14: STOP — the owner tries the sidebar and the CPU pane

**This task ends the turn. Do not start Task 15 until the owner says to go on.** The owner chose this stop (2026-10-24) so they can try the tab before the other panes are built.

**Files:**
- Create: `docs/superpowers/results/2026-10-24-m2-soak.md` (the "Checkpoint" section; Task 20 completes the file)

- [ ] **Step 1: Check the build.** Run `dotnet test` and expect all PASS. Run `dotnet build src/Tmcs -c Debug`.
- [ ] **Step 2: Inspect.** Run the Rider inspections (`lint_files`) on every file changed since commit `9c0a16c`, and fix what they report in those files.
- [ ] **Step 3: Write the checkpoint section** of the results file:
  - What the owner should try.
  - Every "my call" in this plan's Tasks 1–13, one line each, so the owner can check them while trying.
- [ ] **Step 4: Commit** with message `docs: M2 checkpoint notes`.
- [ ] **Step 5: Hand over, then stop.**
  - Tell the owner the Debug build is at `src/Tmcs/bin/Debug/net10.0-windows/win-x64/tmcs.exe`. It is `asInvoker`, so it starts without a UAC prompt, and nothing on the Performance tab needs elevation.
  - Ask them to run it themselves beside Task Manager and compare the sidebar (every entry, the mini graphs, Hide and Show hidden adapters) and the CPU pane. The other entries show only their title and subtitle until Tasks 15–18.
  - **Then end the turn.**
- [ ] **Step 6: When the owner answers:**
  - Record their notes in the results file.
  - Fix what they ask for, if it stays within the spec, each fix with its own test and commit, before Task 15. Ask first about anything outside the spec.
  - Go on to Task 15 only when the owner says so.

---

### Task 15: Memory pane

Read the "Memory composition bar" part of section 3 and section 7 of the look reference first.

**Files:**
- Create: `src/Tmcs/Performance/Views/MemoryPane.cs`
- Modify: `src/Tmcs/Performance/Views/PerformanceView.cs`
- Test: `tests/Tmcs.Tests/Performance/MemoryPaneTests.cs`

**Interfaces:**
- Consumes: `MemoryDevice` (Task 6), the Task 11 pane base.
- Produces `sealed class MemoryPane : PerformancePane`:
  - **The composition block** is fixed at 84 px tall, with its top at info top − 10 − 84.
    - The caption `Memory composition` (Caption font, gray) is at (Left, top).
    - The bar's border is 1 px of 8B12AE with square corners. It sits at y top + 18, is 52 px tall, and spans Left to Right.
    - Inside it, the In use, Modified, Standby and Free segments split the inner width (Right − Left − 2). Each segment's width is round(inner × part / TotalPhysical), half away from zero; Free takes what is left, clamped at 0. That rule is my call; the reference couldn't determine Task Manager's.
    - Fills: In use F4F2F4, Modified EEE6F1, Standby and Free FFFFFF. Every segment after the first starts with a 1-px CEB0D7 line, inside its width.
    - No tooltips. The spec's list doesn't include them (my call).
  - **The usage graph** fills the space above the block: x Left to Right, y GraphTop, height = block top − 21 − GraphTop.
    - Captions `Memory usage` and `Size(TotalPhysical)`, and the bottom captions.
    - `Paint(g, graph, history, 0, TotalPhysical, 3)` with the `Memory` palette.
  - **Info (row flow):**
    - Rows: `In use (Compressed)` (`Size(InUse) (Size(Compressed))`) and `Available` (`Size`); `Committed` (`Pair(Committed, CommitLimit, "GB", 2^30)`) and `Cached` (`Size`); `Paged pool` and `Non-paged pool` (`Size`).
    - Table: `Speed:` (`{SpeedMhz} MHz`), `Slots used:` (`{SlotsUsed} of {Slots}`), `Form factor:`, `Hardware reserved:` (`Size(HardwareReserved)`).
    - An SMBIOS row whose value is unknown (0 or blank) is left out (my call).
  - `internal Rectangle GraphBounds`, `internal Rectangle BarBounds`, `internal (int InUse, int Modified, int Standby, int Free) SegmentWidths()` and `internal InfoLayout BuildInfo()`, for the tests.

- [ ] **Step 1: Write the failing tests.** The pane is 1052×575 at 96 DPI, with a fake memory device.
  - `Rectangles_match_task_manager`: the graph is (19, 69, 1009, 226), the composition caption is at y 316, and the bar's border is (19, 334, 1009, 52).
  - `Segments_split_the_inner_width`: In use 38950 MB, Modified 297, Standby 22875 and Free 3058, with TotalPhysical their sum → 602, 5, 353 and 47.
  - `Info_values`:
    - In use 38.0 GiB with 3.6 GiB compressed gives `38.0 GB (3.6 GB)`. Committed 54.7 of 119.7 GiB gives `55/120 GB`.
    - The table holds `5600 MHz`, `2 of 4`, `DIMM` and `357 MB`. With 0 slots, the `Slots used:` row is left out.
  - `Info_starts_where_task_managers_does`: the info top is 410.
  - `Painting_creates_no_gdi_or_user_objects`: 1000 paints.
- [ ] **Step 2: Run them.** Run `dotnet test --filter MemoryPaneTests`. Expect FAIL.
- [ ] **Step 3: Implement** the pane and give the view's Memory kind this pane.
- [ ] **Step 4: Run the whole suite.** Run `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: memory pane with usage graph and composition bar`.

---

### Task 16: Disk pane

Read sections 4, 6 (the legend markers) and 7 of the look reference first.

**Files:**
- Create: `src/Tmcs/Performance/Views/DiskPane.cs`
- Modify: `src/Tmcs/Performance/Views/PerformanceView.cs`
- Test: `tests/Tmcs.Tests/Performance/DiskPaneTests.cs`

**Interfaces:**
- Consumes: `DiskDevice` (Task 7), `GraphScales` (Task 12), the Task 11 pane base.
- Produces `sealed class DiskPane : PerformancePane`:
  - **Disk transfer rate graph:** fixed at 67 px tall (my call: fixed rather than a share of the space, since the captures have one window size). Its bottom is ChartBottom − 15.
    - Captions `Disk transfer rate` and the scale's caption, plus the bottom captions.
    - The scale is `GraphScales.Disk(max(MaxVisible(1), MaxVisible(2)))`.
    - `Paint(g, graph, history, 1, scale.Max, 3, second: 2)`: read is solid, write dashed.
  - **Active time graph:** from GraphTop down to where the transfer graph's top caption leaves room (the 19-px step from Task 11).
    - Captions `Active time` and `100%`, plus the bottom captions.
    - `Paint(..., 0, 100, 3)`.
  - Task Manager's inner scale marker (`7 MB/s`) is left out. Which value it shows can't be determined (my call).
  - **Info (row flow):**
    - Rows: `Active time` (`Percent`) and `Average response time` (`Milliseconds`); `Read speed` (Solid, 4DA60C) and `Write speed` (Dashed, 79D50D), both `DiskRate`.
    - Table: `Capacity:` (`Size`), `Formatted:` (`Size`, left out when 0, my call), `System disk:` and `Page file:` (`Yes`/`No`), `Type:`.
  - `internal Rectangle ActiveBounds`, `internal Rectangle TransferBounds` and `internal InfoLayout BuildInfo()`.

- [ ] **Step 1: Write the failing tests.** The pane is 1052×575, with a fake disk device.
  - `Rectangles_match_task_manager`: active time is (19, 69, 1009, 263) and transfer rate is (19, 369, 1009, 67). The info top is 461.
  - `Markers_sit_beside_read_and_write`: the read marker's pixels at (19, 515) and (20, 554) are 4DA60C. The write marker's pixel at (107, 515) is 79D50D and at (107, 518) white (a dash gap).
  - `Info_values`: `1%`, `1.6 ms`, `0 KB/s`, `1.3 MB/s`; then `1.8 TB`, `1.8 TB`, `Yes`, `Yes`, `SSD`.
  - `Scale_follows_the_visible_rates`: a 1.3 MiB/s write → the top-right caption is `10 MB/s`.
  - `Painting_creates_no_gdi_or_user_objects`: 1000 paints.
- [ ] **Step 2: Run them.** Run `dotnet test --filter DiskPaneTests`. Expect FAIL.
- [ ] **Step 3: Implement** the pane and give the view's Disk kind this pane.
- [ ] **Step 4: Run the whole suite.** Run `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: disk pane with active time and transfer rate graphs`.

---

### Task 17: Network pane

Read sections 6 (the legend markers and the Wi-Fi signal glyph) and 7 ("Network width only grows") of the look reference first.

**Files:**
- Create: `src/Tmcs/Performance/Views/NetworkPane.cs`
- Modify: `src/Tmcs/Performance/Views/PerformanceView.cs`
- Test: `tests/Tmcs.Tests/Performance/NetworkPaneTests.cs`

**Interfaces:**
- Consumes: `NetworkDevice` (Task 8), `GraphScales` (Task 12), the Task 11 pane base.
- Produces `sealed class NetworkPane : PerformancePane`:
  - **Throughput graph:** from GraphTop to ChartBottom − 15.
    - Captions `Throughput` and the scale's caption, plus the bottom captions.
    - The scale is `GraphScales.Network(max(MaxVisible(0), MaxVisible(1)))`.
    - `Paint(g, graph, history, 0, scale.Max, 3, second: 1)`: receive is solid, send dashed.
    - No inner scale marker (as for disks).
  - **Info (row flow):**
    - Rows: `Send` (Dashed, DF672C), then `Receive` (Solid, A74F01) on its own row, both `BitRate`.
    - `minMarkedWidth` is `int _widestRate`: the widest Send or Receive value this pane has drawn. It only grows. It is one field on the pane, which every adapter shares, as Task Manager's column is.
  - **Table, Ethernet:** `Adapter name:` (`Info.Alias`), `DNS name:`, `Connection type:`, `IPv4 address:`, `IPv6 address:`.
  - **Table, Wi-Fi:** `Adapter name:`, `SSID:`, `DNS name:`, `Connection type:`, `IPv4 address:`, `IPv6 address:`, `Signal strength:`.
    - The DNS row's place on Wi-Fi is my call: none was captured.
    - The signal row is 19 px tall, and its value is drawn as the glyph below.
  - **Rows left out:** DNS name when the suffix is empty, as Task Manager does. An address that is null (my call). SSID and Signal strength when `Wifi` is null.
  - **Signal glyph:** our own drawing, not Task Manager's bitmap. It is 16×16 at (value x − 2, row y).
    - Five bars, each 4 px wide and sharing its outline with the next: x 0–3, 3–6, 6–9, 9–12 and 12–15.
    - The bars' tops are at y 10, 7, 5, 2 and 0, and all end at y 13. A 1-px baseline runs at y 15.
    - Outlines are BB814E. The first `NetworkDevice.SignalBars(quality)` bars are filled DBBAA0; the rest are white.
  - `internal Rectangle GraphBounds`, `internal InfoLayout BuildInfo()`, `internal int WidestRate`.

- [ ] **Step 1: Write the failing tests.** The pane is 1052×575, with fake adapters.
  - `Ethernet_rectangles`: the graph is (19, 69, 1009, 367), the info top is 461, and the send marker's pixel at (19, 464) is DF672C. The receive marker's pixel at (19, 515) is A74F01.
  - `Wifi_rectangles`: the graph is (19, 69, 1009, 352) and the info top is 446.
  - `Table_columns_follow_the_widest_rate_seen`: after drawing a value 86 px wide, a Wi-Fi adapter whose values are 60 px wide gets table keys at x 125 and values at x 230.
  - `Dns_row_is_left_out_when_empty`.
  - `Signal_glyph_fills_one_bar_per_20_percent`: at quality 60, the glyph at (228, 536) has DBBAA0 inside bar 4 at (10, 9) and white inside bar 5 at (13, 9).
  - `Idle_scale_is_100_Kbps`.
  - `Painting_creates_no_gdi_or_user_objects`: 1000 paints.
- [ ] **Step 2: Run them.** Run `dotnet test --filter NetworkPaneTests`. Expect FAIL.
- [ ] **Step 3: Implement** the pane and give the view's Network kind this pane.
- [ ] **Step 4: Run the whole suite.** Run `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: network pane with throughput graph and adapter details`.

---

### Task 18: GPU pane

Read the "GPU pane" parts of sections 1, 4 and 7 of the look reference first.

**Files:**
- Create: `src/Tmcs/Performance/Views/GpuPane.cs`
- Modify: `src/Tmcs/Performance/Views/PerformanceView.cs`
- Test: `tests/Tmcs.Tests/Performance/GpuPaneTests.cs`

**Interfaces:**
- Consumes: `GpuDevice` (Task 9), the Task 11 pane base.
- Produces `sealed class GpuPane : PerformancePane`:
  - `ContentHeight => 615`. The content overflows Task Manager's 575-px pane, so the native scrollbar shows and `Right` moves 17 px left.
  - The vertical layout is fixed, because it scrolls; only widths flex.
  - **Engine graphs (2 × 2):**
    - The engine area spans x Left + 4 to Right − 4. The two columns are ⌊(area width − 8) / 2⌋ wide, with an 8-px gap.
    - Row 1: labels at y 55, graphs at y 77, 73 px tall. Row 2: labels at y 156, graphs at y 178, 74 px tall.
    - Engines: 3D (top left), Copy (top right), Video Encode, Video Decode. They are series 1–4, `Paint(..., 100, 4)`, `Gpu` palette.
    - Each name is in the Engine font, black, at (graph.X + 13, label y).
    - Task Manager's chevron is left out: it opens the per-graph engine dropdowns, which are out of scope. The name keeps its place (my call).
    - Each engine's percent (`Percent` of the latest value) is in the Caption font, gray, at the label y + 2, right-aligned to graph.Right + 2.
  - **Memory graphs:**
    - Both span x Left + 2 to Right − 2. Dedicated is at y 279, Shared at y 366, each 67 px tall.
    - Top captions only: `Dedicated GPU memory usage` with `Size(DedicatedTotal)`, and `Shared GPU memory usage` with `Size(SharedTotal)`.
    - Series 5 with max `DedicatedTotal`, and series 6 with max `SharedTotal`. Grid step 3.
  - **Info (columns layout):**
    - Column 1: `Utilization` (`Percent`) and `GPU Memory` (`Pair` of dedicated + shared used over dedicated + shared total, `"GB"`, 2^30).
    - Column 2: `Dedicated GPU memory` and `Shared GPU memory` (`Pair`, `"GB"`), then `GPU Temperature` (`Celsius`; the row is left out when there is no temperature).
    - Table: `Driver version:`, `Driver date:`, `DirectX version:`, `Physical location:`, `Hardware reserved memory:` (`Size`).
  - `internal Rectangle EngineBounds(int engine)`, `internal Rectangle DedicatedBounds`, `internal Rectangle SharedBounds`, `internal InfoLayout BuildInfo()`.

- [ ] **Step 1: Write the failing tests.** The pane is 1052×575, with a fake GPU device.
  - `Rectangles_match_task_manager`:
    - The scrollbar shows, and `Right == 1011`.
    - The engines are (23, 77, 488, 73), (519, 77, 488, 73), (23, 178, 488, 74) and (519, 178, 488, 74).
    - The memory graphs are (21, 279, 988, 67) and (21, 366, 988, 67). The info top is 450.
  - `Info_values`:
    - Dedicated 3.0 of 24.0 GiB and shared 0.3 of 31.8 GiB give `3.0/24.0 GB`, `0.3/31.8 GB` and `3.3/55.8 GB`.
    - 39.2 °C gives `39 °C`, and 254 MiB of hardware-reserved memory gives `254 MB`.
    - With no temperature, the `GPU Temperature` item is left out.
  - `Scrolling_moves_the_content`: scrolled down 40 px, the dedicated graph's left border pixel at (21, 300 − 40) is the GPU border 117DBB, and (21, 300) is not.
  - `Painting_creates_no_gdi_or_user_objects`: 1000 paints.
- [ ] **Step 2: Run them.** Run `dotnet test --filter GpuPaneTests`. Expect FAIL.
- [ ] **Step 3: Implement** the pane and give the view's GPU kind this pane.
- [ ] **Step 4: Run the whole suite.** Run `dotnet test`. Expect all PASS.
- [ ] **Step 5: Commit** with message `feat: GPU pane with engine, memory and temperature`.

---

### Task 19: Soak support: pane scenario steps, device diagnostics, GPU churn

The soak (Task 20) must hold the CPU, Network and GPU panes for the spec's minimum time and prove that the GPU's PDH instances stay bounded under GPU process churn. The cmd/ping churn creates no GPU instances, so it needs a GPU-using churner.

**Files:**
- Modify: `src/Tmcs/Diagnostics/ScenarioParser.cs`, `src/Tmcs/MainForm.cs` (`IScenarioTarget.Apply`), `src/Tmcs/Diagnostics/DiagSnapshot.cs`, `src/Tmcs/Diagnostics/DiagLog.cs`, `src/Tmcs/Diagnostics/TestHarness.cs`
- Modify: `tools/churn.ps1`, `tools/fixtures/soak-pass.csv`, `tools/fixtures/soak-fail.csv`, `tmcs.slnx` (add the tool project)
- Create: `tools/gpu-touch/GpuTouch.csproj`, `tools/gpu-touch/Program.cs`
- Test: `tests/Tmcs.Tests/Diagnostics/DiagnosticsTests.cs`, `tests/Tmcs.Tests/MainFormTests.TestMode.cs`

**Interfaces:**
- Consumes: `Sidebar.SelectFirst` (Task 12), `PerformanceModel.GpuInstances` and `Devices` (Task 10), the M1 diagnostics.
- Produces:
  - `ScenarioAction` gains `Cpu`, `Network` and `Gpu`, named `cpu`, `network` and `gpu`. The parser's usage text lists every action.
  - `IScenarioTarget.Apply` in `MainForm`:
    - `cpu`, `network` and `gpu`: restored (as `details` already restores), Performance tab, Normal speed. Then `Sidebar.SelectFirst` of that kind: the first listed, non-hidden adapter for `network`, and GPU 0 for `gpu`.
      - When nothing of that kind is listed, it throws `InvalidOperationException` naming the kind. That stops test mode with exit code 5, so a soak can never silently hold the wrong pane.
    - `paused` now means: restored, Performance tab, CPU pane, paused. M1 proved pausing on the Details tab; the Performance tab is where views could keep working while paused. `details` and `minimized` are unchanged.
    - Every Performance step ends with `PerformanceView.Update()`, so `switch_ms` covers the repaint.
  - `DiagSnapshot` gains two last fields, `int Devices` and `int GpuInstances`. The `DiagLog.Header` ends `…,cpu_ms,switch_ms,devices,gpu_instances`, and `TestHarness.Probe` fills them from the model.
    - An M1 CSV at the same path is now refused (its header differs), so M2 soaks write `m2-*.csv`.
    - `soak-report.ps1` reads columns by name and checks every segment not named `done` or `none`, so it needs no change. Add the two columns to both fixtures, with constant values.
  - `tools/gpu-touch`, a console project:
    - `OutputType Exe`, `net10.0-windows`, `RuntimeIdentifier win-x64`, not self-contained, `AssemblyName gpu-touch`, `AllowUnsafeBlocks` for `LibraryImport`.
    - It is in `tmcs.slnx`, so `dotnet build` covers it, and is never published with tmcs.
    - It calls `D3D11CreateDevice(adapter 0, D3D_DRIVER_TYPE_HARDWARE = 1, 0, 0, null, 0, D3D11_SDK_VERSION = 7, out device, out level, out context)` from `d3d11.dll`.
      - The device pointer must not be null: a null-device probe creates no GPU Engine instances. A real device adds 13 on this machine.
    - Then it sleeps 500 ms, releases the context and device, and exits with 0, or with 1 if creation failed. No window.
  - `tools/churn.ps1` gains two parameters:
    - `-GpuParallel` (0–64, default 0): that many gpu-touch processes kept running beside the cmd churners, each replaced as it exits.
    - `-GpuTouch`: the path, by default `$PSScriptRoot/gpu-touch/bin/Release/net10.0-windows/win-x64/gpu-touch.exe`. The script stops with an error if the file is missing and `-GpuParallel` is above 0.
    - The final line adds the gpu-touch count when `-GpuParallel` is above 0. With 0 the script behaves exactly as before.

- [ ] **Step 1: Write the failing tests.**
  - `DiagnosticsTests`:
    - `Parse_scenario_spec` also parses `cpu:12,network:12,gpu:12,paused:1`, and `Name` round-trips every action.
    - `Diag_format_is_invariant_csv`: the example snapshot gains `Devices` 20 and `GpuInstances` 512, so the line ends `,3.25,20,512`. The header has 17 fields.
  - `MainFormTests.TestMode`:
    - `Cpu_step_shows_the_cpu_pane_at_normal_speed`.
    - `Network_step_selects_the_first_visible_adapter`: adapter A is hidden → B is selected.
    - `Gpu_step_without_a_gpu_throws`.
    - `Paused_step_pauses_on_the_cpu_pane`. Update M1's `Test_mode_apply_paused_then_details` to the new meaning of `paused`.
- [ ] **Step 2: Run them.** Run `dotnet test --filter "DiagnosticsTests|MainFormTests"`. Expect the new tests to FAIL.
- [ ] **Step 3: Implement** the actions, the diagnostics columns, the fixtures, gpu-touch and the churn parameters.
- [ ] **Step 4: Verify.**
  - Run `dotnet test`. Expect all PASS.
  - Run `pwsh -File tools/soak-report.ps1 -Csv tools/fixtures/soak-pass.csv`. Expect exit code 0.
  - Run `pwsh -File tools/soak-report.ps1 -Csv tools/fixtures/soak-fail.csv`. Expect exit code 1, with private bytes and gdi failing as before.
  - Run `dotnet build tools/gpu-touch -c Release`. Then run `tools/gpu-touch/bin/Release/net10.0-windows/win-x64/gpu-touch.exe` and check that `$LASTEXITCODE` is 0.
  - Run `pwsh -File tools/churn.ps1 -Parallel 1 -GpuParallel 2 -Minutes 0.2`. Expect a gpu-touch count above 10 and no windows.
- [ ] **Step 5: Commit** with message `feat: performance scenario steps, device diagnostics and GPU churn`.

---

### Task 20: Publish and milestone-2 acceptance

Run spec section 9's tests for the whole milestone. M1's Task 13 and its results file (`docs/superpowers/results/2026-10-23-m1-soak.md`) show how each was run. Follow them except where this task says otherwise.

**Files:**
- Modify: `docs/superpowers/results/2026-10-24-m2-soak.md` (begun in Task 14), `README.md` (the new scenario actions, `-GpuParallel`)

**The scenario,** used by both soaks: `cpu:12,network:12,gpu:12,details:12,minimized:12,cpu:3,paused:10,cpu:2` (75 minutes).
- Each soaked state gets 10 minutes after the 2-minute warm-up (spec test 1).
- The `details` step comes after 36 minutes on the Performance tab, so its first row's `switch_ms` is spec test 2.
- `minimized` → `cpu` checks restoring.
- `paused` holds the CPU pane for spec test 3.

- [ ] **Step 1: Build.** Run `dotnet test` and expect all PASS. Run `dotnet publish src/Tmcs -p:PublishProfile=win-x64`, `dotnet build -c Debug` and `dotnet build tools/gpu-touch -c Release`.
- [ ] **Step 2: Ask first.** Tell the owner in chat that two tmcs windows will sit behind their windows for about 80 minutes without taking focus, and that the churn includes short GPU processes. Wait for their OK.
- [ ] **Step 3: Churn.** Start `pwsh -File tools/churn.ps1 -Parallel 8 -GpuParallel 2 -Minutes 80` in the background.
- [ ] **Step 4: Non-elevated soak.** Run the Debug build with the scenario and `TMCS_DIAG` set to `artifacts/soak/m2-debug.csv`, as in M1's Task 13 Step 3.
- [ ] **Step 5: Elevated soak.**
  - Tell the owner a UAC prompt is coming, and wait for "ready".
  - Then launch the published build elevated with `artifacts/soak/m2-elevated.csv`, as in M1's Task 13 Step 4.
- [ ] **Step 6: Score.**
  - Run `soak-report.ps1` on each CSV. Expect exit code 0.
  - Also report, per segment, the `gpu_instances` range (it should track the live GPU processes, not grow) and whether `devices` stayed constant. Explain any change in `devices`.
- [ ] **Step 7: Idle cost (spec test 4), on each tab.**
  - Take tmcs's CPU ms per minute from the elevated CSV's `cpu` and `details` segments.
  - Ask the owner to put the stock Task Manager on the Performance tab with the CPU pane, at Normal speed, then later on the Details tab. Announce each change and wait for "ready".
  - Measure `(Get-Process Taskmgr).TotalProcessorTime` over 5 minutes in each state.
  - Expect tmcs ≤ Task Manager on each tab.
- [ ] **Step 8: Icon dedup (spec test 5).** Repeat M1's procedure from its results file on the Debug build. Expect the same outcome: one slot for 50 copies, reclaimed after they exit.
- [ ] **Step 9: Fidelity (spec test 6).**
  - Ask the owner to run `artifacts/publish/tmcs.exe` themselves and compare every pane and the sidebar with Task Manager, side by side.
  - Record their notes. Fidelity fixes become follow-up tasks, never silent changes.
- [ ] **Step 10: Record the results.** Complete the results file: the churn counts, every report table, the idle-cost numbers, icon dedup, the fidelity notes, and the list of calls for the owner to check.
  - On any FAIL, stop and use superpowers:systematic-debugging. Fix it, and re-run the failed soak before going on.
- [ ] **Step 11: Commit** with message `docs: milestone 2 soak results`. `artifacts/` stays untracked. Don't push without the owner's OK.
