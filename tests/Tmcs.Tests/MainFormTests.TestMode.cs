using System.Diagnostics;
using System.Runtime.InteropServices;
using Tmcs.Details;
using Tmcs.Diagnostics;
using Tmcs.Native;
using Tmcs.Sampling;
using Tmcs.Settings;
using Tmcs.Ui;

namespace Tmcs.Tests;

// Test mode (TMCS_DIAG set): the form the leak soak drives. Every form is still an OffscreenMainForm, and each scenario action
// below is checked to leave it off-screen and inactive.
public sealed partial class MainFormTests
{
    private const uint GwHwndNext = 2;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoMove = 0x2;

    /// <summary>The flags of the sink to the bottom: no activation, no move, no resize.</summary>
    private const uint Sink = SwpNoActivate | SwpNoMove | SwpNoSize;

    private string DiagPath => Path.Combine(_dir, "diag", "run.csv"); // its directory does not exist yet

    private LaunchOptions TestMode(string? scenario = null) => new(DiagPath, scenario);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint hwnd, uint command);

    /// <summary>
    /// Whether <paramref name="window"/> lies below <paramref name="other"/> in the z-order. Windows reports both a sink to the
    /// bottom and a background process's topmost move as "after some window", so the resulting order is what tests check.
    /// </summary>
    private static bool IsBelow(nint window, nint other)
    {
        for (var w = GetWindow(other, GwHwndNext); w != 0; w = GetWindow(w, GwHwndNext))
            if (w == window)
                return true;
        return false;
    }

    [Fact]
    public void Test_mode_stays_below_other_windows_ignoring_saved_always_on_top_and_speed() => Sta.Run(() =>
    {
        var saved = AppSettings.CreateDefault();
        saved.AlwaysOnTop = true;
        saved.UpdateSpeed = UpdateSpeed.Low;
        saved.WindowX = 100;
        saved.WindowY = 100;
        saved.WindowWidth = 900;
        saved.WindowHeight = 600;
        new SettingsStore(SettingsPath).Save(saved);
        var bytes = File.ReadAllBytes(SettingsPath);
        using var other = new OffscreenForm(); // stands in for the owner's windows; a new window would land above it
        other.ShowOffscreen();

        using var rig = new Rig(SettingsPath, TestMode());
        Assert.True(IsBelow(rig.Form.Handle, other.Handle), "not sunk before it first showed"); // Handle creates the window, unshown
        Assert.Equal(1, rig.Form.ZOrderChangesAtCreation); // the sink, no topmost move
        rig.Form.ShowOffscreen();

        Assert.True(IsBelow(rig.Form.Handle, other.Handle), "not below other windows once shown");
        Assert.Equal(Sink, rig.Form.ZOrderChanges[^1] & Sink); // sunk again after showing
        Assert.All(rig.Form.ZOrderChanges, flags => Assert.Equal(SwpNoActivate, flags & SwpNoActivate));
        Assert.False(Checked(rig.Form, CommandIds.AlwaysOnTop));
        Assert.Equal(UpdateSpeed.Normal, rig.Form.Clock.Speed); // whatever was saved
        Assert.Equal(1000, rig.Timer.IntervalMs);
        Assert.True(rig.Timer.Enabled);
        var area = OffscreenMainForm.OffscreenArea;
        Assert.Equal(new Rectangle(area.X + (area.Width - 900) / 2, area.Y + (area.Height - 600) / 2, 900, 600), rig.Form.WindowRect);

        rig.Form.Close();

        Assert.Equal(bytes, File.ReadAllBytes(SettingsPath)); // not saved
    });

    [Fact]
    public void Test_mode_apply_paused_then_details() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath, TestMode());
        rig.Form.ShowOffscreen();
        IScenarioTarget target = rig.Form;

        target.Apply(ScenarioAction.Paused);

        Assert.Equal(UpdateSpeed.Paused, rig.Form.Clock.Speed);
        Assert.False(rig.Timer.Enabled);
        Assert.Equal(FormWindowState.Normal, rig.Form.WindowState);
        rig.Form.AssertOffscreenAndInactive();
        var reads = rig.Snapshots.Reads;
        rig.Timer.Fire();
        Assert.Equal(reads, rig.Snapshots.Reads);

        target.Apply(ScenarioAction.Details);

        Assert.Equal(UpdateSpeed.Normal, rig.Form.Clock.Speed);
        Assert.Equal(1000, rig.Timer.IntervalMs);
        Assert.True(rig.Timer.Enabled);
        Assert.Equal(FormWindowState.Normal, rig.Form.WindowState);
        rig.Form.AssertOffscreenAndInactive();
    });

    [Fact]
    public void Test_mode_apply_minimized_then_details_restores_at_once_without_activation() => Sta.Run(() =>
    {
        using var other = new OffscreenForm(); // stands in for the owner's windows
        other.ShowOffscreen();
        using var rig = new Rig(SettingsPath, TestMode());
        rig.Form.ShowOffscreen();
        IScenarioTarget target = rig.Form;
        target.Apply(ScenarioAction.Paused);

        target.Apply(ScenarioAction.Minimized);
        Application.DoEvents();

        Assert.Equal(FormWindowState.Minimized, rig.Form.WindowState);
        Assert.Equal(UpdateSpeed.Normal, rig.Form.Clock.Speed);
        Assert.True(rig.Timer.Enabled);
        rig.Form.AssertOffscreenAndInactive();

        rig.Snapshots.Processes.RemoveAll(p => p.Pid == 2);
        rig.Form.Clock.RefreshNow(); // sampled while minimized: the view is left alone
        Assert.Equal(3, rig.Form.Details.DisplayedRows.Count);
        var reads = rig.Snapshots.Reads;
        var changes = rig.Form.ZOrderChanges.Count;

        target.Apply(ScenarioAction.Details);

        // Synchronous: restored and showing the current table before Apply returns, so the runner's stopwatch covers it.
        Assert.Equal(FormWindowState.Normal, rig.Form.WindowState);
        Assert.Equal([1, 3], rig.Form.Details.DisplayedRows.Select(r => r.Id.Pid).Order());
        Assert.Equal(reads, rig.Snapshots.Reads);
        Assert.Equal(UpdateSpeed.Normal, rig.Form.Clock.Speed);
        Assert.True(rig.Form.ZOrderChanges.Count > changes);
        Assert.Equal(Sink, rig.Form.ZOrderChanges[^1] & Sink); // sunk again after the restore
        Assert.True(IsBelow(rig.Form.Handle, other.Handle), "not below other windows after the restore");
        Application.DoEvents();
        rig.Form.AssertOffscreenAndInactive();
    });

    [Fact]
    public void Test_mode_never_saves_settings() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath, TestMode());
        rig.Form.ShowOffscreen();
        IScenarioTarget target = rig.Form;

        // Each of these schedules the 2 s save outside test mode.
        target.Apply(ScenarioAction.Minimized);
        target.Apply(ScenarioAction.Details);
        rig.Form.Details.SortBy(DetailsColumn.Pid);
        rig.Form.Command(CommandIds.SpeedLow);
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 2500)
        {
            Thread.Sleep(20);
            Application.DoEvents();
        }

        rig.Form.Close();

        Assert.False(File.Exists(SettingsPath));
    });

    [Fact]
    public void Test_mode_probe_reports_the_form_and_process_counts() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath, TestMode());
        rig.Form.ShowOffscreen();
        var self = Kernel32.GetCurrentProcess();

        var before = DateTime.UtcNow;
        var s = rig.Form.Harness!.Probe();

        Assert.Equal(DateTimeKind.Utc, s.Utc.Kind);
        Assert.InRange(s.Utc, before, DateTime.UtcNow);
        Assert.Equal("none", s.Step); // no scenario
        Assert.Equal(rig.Form.Clock.SamplesTaken, s.Samples);
        Assert.Equal(3, s.Rows);
        Assert.Equal(rig.Form.Icons.SlotCount, s.IconSlots);
        Assert.Equal(3, s.IconLive);
        Assert.Equal(0, s.IconPooled);
        Assert.Equal(0, s.SidCache); // FakeInfo caches nothing
        Assert.True(s.PrivateBytes > 0);
        Assert.True(s.ManagedBytes > 0);
        Assert.True(s.Handles > 0);
        Assert.Equal((int)User32.GetGuiResources(self, 0), s.Gdi); // GR_GDIOBJECTS
        Assert.Equal((int)User32.GetGuiResources(self, 1), s.User); // GR_USEROBJECTS
        Assert.True(s.CpuMs > 0);
        Assert.Equal(0, s.SwitchMs);
        Assert.True(File.Exists(DiagPath)); // the log was started, its directory created
    });

    [Fact]
    public void Test_mode_scenario_runs_to_the_end_then_closes_the_form_and_the_log() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath, TestMode("paused:0.001,details:0.001")); // 60 ms each

        rig.Form.ShowOffscreen(); // starts the scenario once the form has shown
        var clock = Stopwatch.StartNew();
        while (!rig.Form.IsDisposed && clock.ElapsedMilliseconds < 5000)
        {
            Thread.Sleep(10);
            Application.DoEvents();
        }

        Assert.True(rig.Form.IsDisposed, "the form did not close within 5 s of starting the scenario");
        using (new FileStream(DiagPath, FileMode.Open, FileAccess.Read, FileShare.None)) // the log has closed the file
        {
        }

        Assert.Equal([DiagLog.Header], File.ReadAllLines(DiagPath)); // the 10 s log timer never came round
        Assert.False(File.Exists(SettingsPath));
    });
}
