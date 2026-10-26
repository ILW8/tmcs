using System.Diagnostics;
using System.Runtime.InteropServices;
using Tmcs.Details;
using Tmcs.Icons;
using Tmcs.Native;
using Tmcs.Performance;
using Tmcs.Processes;
using Tmcs.Sampling;
using Tmcs.Settings;
using Tmcs.Ui;

namespace Tmcs.Tests;

// Every form here is an OffscreenMainForm: off-screen, never activated, no taskbar entry. Its helpers, and the rig's Dispose,
// assert that after each interaction. No test opens a menu or a dialog.
public sealed partial class MainFormTests : IDisposable
{
    // Values the tests check are written out here rather than taken from production code, so a wrong constant shows up.
    private const uint WmInitMenuPopup = 0x0117;
    private const uint MfByPosition = 0x400;
    private const uint MfChecked = 0x8;
    private const uint SwpNoActivate = 0x10;
    private const uint WmDeviceChange = 0x0219;
    private const nint DbtDevNodesChanged = 0x0007;
    private const nint DbtDeviceArrival = 0x8000;
    private const nint DbtDeviceRemoveComplete = 0x8004;

    // The directory does not exist until a form saves, so a test can tell whether anything was written.
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tmcs-tests-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetMenu(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetSubMenu(nint menu, int position);

    [LibraryImport("user32.dll")]
    private static partial int GetMenuItemCount(nint menu);

    [LibraryImport("user32.dll")]
    private static partial uint GetMenuItemID(nint menu, int position);

    [LibraryImport("user32.dll")]
    private static partial uint GetMenuState(nint menu, uint item, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMenuStringW")]
    private static unsafe partial int GetMenuString(nint menu, uint item, char* buffer, int capacity, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsMenu(nint menu);

    [Fact]
    public void First_sample_populates_details_rows() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        SelectTab(rig.Form, "Details");

        rig.Form.ShowOffscreen();

        Assert.Equal(3, rig.Form.Details.DisplayedRows.Count);
        Assert.Equal(3, rig.Form.Table.Count);
        Assert.Equal(1, rig.Snapshots.Reads);
        Assert.Equal(3, rig.Form.Icons.LiveCount);
    });

    [Fact]
    public void Window_has_title_tmcs_performance_and_details_tabs_and_a_dpi_scaled_minimum_size() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();

        Assert.Equal("tmcs", rig.Form.Text);
        var tabs = Tabs(rig.Form);
        Assert.Equal(["Performance", "Details"], tabs.TabPages.Cast<TabPage>().Select(p => p.Text));
        Assert.Same(rig.Form.PerformanceView, tabs.TabPages[0].Controls.Cast<Control>().Single());
        Assert.Same(rig.Form.Details, tabs.TabPages[1].Controls.Cast<Control>().Single());
        var dpi = rig.Form.DeviceDpi;
        Assert.Equal(new Size(Scale(400, dpi), Scale(300, dpi)), rig.Form.MinimumSize);
        Assert.Equal(new Size(1294, 712), rig.Form.Size); // first run: the default window size
    });

    [Fact]
    public void Startup_applies_saved_speed_always_on_top_and_clamped_position() => Sta.Run(() =>
    {
        var saved = AppSettings.CreateDefault();
        saved.UpdateSpeed = UpdateSpeed.High;
        saved.AlwaysOnTop = true;
        saved.WindowX = 100; // on the owner's screen, but not inside the form's only working area
        saved.WindowY = 100;
        saved.WindowWidth = 900;
        saved.WindowHeight = 600;
        new SettingsStore(SettingsPath).Save(saved);

        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();

        Assert.Equal(UpdateSpeed.High, rig.Form.Clock.Speed);
        Assert.Equal(500, rig.Timer.IntervalMs);
        Assert.True(rig.Timer.Enabled);
        Assert.Equal(1, rig.Form.ZOrderChangesAtCreation); // the topmost move, made as the window was created, before it showed
        Assert.All(rig.Form.ZOrderChanges, flags => Assert.Equal(SwpNoActivate, flags & SwpNoActivate)); // none activates the window
        Assert.True(Checked(rig.Form, CommandIds.AlwaysOnTop));
        var area = OffscreenMainForm.OffscreenArea;
        Assert.Equal(new Rectangle(area.X + (area.Width - 900) / 2, area.Y + (area.Height - 600) / 2, 900, 600), rig.Form.WindowRect);
    });

    [Fact]
    public void Saved_size_below_the_minimum_gives_a_window_of_the_minimum_size() => Sta.Run(() =>
    {
        var saved = AppSettings.CreateDefault();
        saved.WindowX = 100; // goes through the off-screen working area, like any saved position
        saved.WindowY = 100;
        saved.WindowWidth = 10;
        saved.WindowHeight = 10;
        new SettingsStore(SettingsPath).Save(saved);

        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();

        var dpi = rig.Form.DeviceDpi;
        var minimum = new Size(Scale(400, dpi), Scale(300, dpi));
        Assert.Equal(minimum, rig.Form.MinimumSize);
        Assert.Equal(minimum, rig.Form.WindowRect.Size);
        Assert.True(OffscreenMainForm.OffscreenArea.Contains(rig.Form.WindowRect));
    });

    [Fact]
    public void Offscreen_form_refuses_settings_that_would_maximize_it() => Sta.Run(() =>
    {
        var saved = AppSettings.CreateDefault();
        saved.Maximized = true;
        new SettingsStore(SettingsPath).Save(saved);
        var icons = new FakeIconLoader();
        var generic = icons.CreateGeneric();
        var services = new AppServices(new FakeSnapshots(), new FakeInfo(), icons, generic, new FakeTimer(), new SettingsStore(SettingsPath), new LaunchOptions(null, null), FakeDeviceSources.Create());

        Assert.Throws<InvalidOperationException>(() => new OffscreenMainForm(services));

        icons.DestroyIcon(generic); // refused before MainForm took it
    });

    [Fact]
    public void Menu_bar_matches_task_manager_and_shows_current_checks() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();

        var bar = GetMenu(rig.Form.Handle);
        Assert.True(IsMenu(bar));
        Assert.Equal(["&File", "&Options", "&View"], Texts(bar));
        var file = GetSubMenu(bar, 0);
        var options = GetSubMenu(bar, 1);
        var view = GetSubMenu(bar, 2);
        Assert.Equal(["E&xit"], Texts(file));
        Assert.Equal([CommandIds.Exit], Ids(file));
        Assert.Equal(["&Always on top"], Texts(options));
        Assert.Equal([CommandIds.AlwaysOnTop], Ids(options));
        Assert.Equal(["&Refresh now", "&Update speed"], Texts(view));
        Assert.Equal(CommandIds.RefreshNow, (int)GetMenuItemID(view, 0));
        var speed = GetSubMenu(view, 1);
        Assert.Equal(["&High", "&Normal", "&Low", "&Paused"], Texts(speed));
        Assert.Equal([CommandIds.SpeedHigh, CommandIds.SpeedNormal, CommandIds.SpeedLow, CommandIds.SpeedPaused], Ids(speed));

        User32.SendMessage(rig.Form.Handle, WmInitMenuPopup, speed, 1);
        Assert.Equal([false, true, false, false], Checks(speed));
        User32.SendMessage(rig.Form.Handle, WmInitMenuPopup, options, 1);
        Assert.Equal([false], Checks(options));

        rig.Form.Command(CommandIds.SpeedPaused);
        rig.Form.Command(CommandIds.AlwaysOnTop);
        User32.SendMessage(rig.Form.Handle, WmInitMenuPopup, speed, 1);
        Assert.Equal([false, false, false, true], Checks(speed));
        User32.SendMessage(rig.Form.Handle, WmInitMenuPopup, options, 1);
        Assert.Equal([true], Checks(options));
    });

    [Fact]
    public void Speed_menu_command_changes_clock() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();

        rig.Form.Command(CommandIds.SpeedLow);

        Assert.Equal(UpdateSpeed.Low, rig.Form.Clock.Speed);
        Assert.Equal(4000, rig.Timer.IntervalMs);
        Assert.True(rig.Timer.Enabled);

        rig.Form.Command(CommandIds.SpeedHigh);
        Assert.Equal(UpdateSpeed.High, rig.Form.Clock.Speed);
        Assert.Equal(500, rig.Timer.IntervalMs);
    });

    [Fact]
    public void Paused_takes_no_snapshots() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        rig.Timer.Fire(); // running: a tick takes a sample
        Assert.Equal(2, rig.Snapshots.Reads);

        rig.Form.Command(CommandIds.SpeedPaused);
        rig.Timer.Fire();
        rig.Timer.Fire();
        rig.Timer.Fire();

        Assert.Equal(UpdateSpeed.Paused, rig.Form.Clock.Speed);
        Assert.False(rig.Timer.Enabled);
        Assert.Equal(2, rig.Snapshots.Reads);
    });

    [Fact]
    public void Refresh_now_and_F5_take_one_sample_even_while_paused() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        SelectTab(rig.Form, "Details");
        rig.Form.Command(CommandIds.SpeedPaused);
        rig.Snapshots.Processes.RemoveAt(0);

        rig.Form.Command(CommandIds.RefreshNow);
        Assert.Equal(2, rig.Snapshots.Reads);
        Assert.Equal(2, rig.Form.Details.DisplayedRows.Count);

        Assert.True(rig.Form.PressKey(Keys.F5));
        Assert.Equal(3, rig.Snapshots.Reads);
        Assert.False(rig.Form.PressKey(Keys.F6));
        Assert.Equal(3, rig.Snapshots.Reads);
    });

    [Fact]
    public void Ticks_are_grid_beats_and_quick_refreshes_are_not() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        var beats = rig.Form.Performance.Beats;
        Assert.Equal(1, beats.Count); // the first sample, as the window loaded

        rig.Timer.Fire();
        Assert.Equal(2, beats.Count);
        for (var i = 0; i < 3; i++) Assert.True(rig.Form.PressKey(Keys.F5)); // well within a second of the tick
        Assert.Equal(2, beats.Count);
        rig.Timer.Fire();
        Assert.Equal(3, beats.Count);
    });

    [Fact]
    public void Every_sample_feeds_the_model_on_any_tab_and_minimized() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        var history = rig.Form.Performance.Cpu.History;
        Assert.Equal(1, history.Count); // the first sample, as the window loaded

        var tabs = Tabs(rig.Form);
        var expected = history.Count;
        foreach (TabPage page in tabs.TabPages)
        {
            SelectTab(rig.Form, page.Text);
            rig.Timer.Fire();
            Assert.Equal(++expected, history.Count);
        }

        rig.Form.MinimizeWithoutActivation();
        rig.Timer.Fire();
        Assert.Equal(++expected, history.Count);
    });

    [Fact]
    public void Opens_on_the_performance_tab() => Sta.Run(() =>
    {
        using (var rig = new Rig(SettingsPath))
        {
            rig.Form.ShowOffscreen();

            Assert.Equal("Performance", Tabs(rig.Form).SelectedTab!.Text);
            SelectTab(rig.Form, "Details");
            rig.Form.Close(); // saves the settings
        }

        using var again = new Rig(SettingsPath);
        again.Form.ShowOffscreen();
        Assert.Equal("Performance", Tabs(again.Form).SelectedTab!.Text); // the selected tab is not remembered
    });

    [Fact]
    public void Hidden_views_have_their_windows_once_the_form_has_shown() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();

        var details = rig.Form.Details;
        var list = (ListView)details.Controls.Cast<Control>().Single();
        Assert.False(details.Visible);
        Assert.True(list.IsHandleCreated, "the Details list has no window yet");
        Assert.All(rig.Form.PerformanceView.Panes, p => Assert.True(p.IsHandleCreated, $"{p.GetType().Name} has no window yet"));

        SelectTab(rig.Form, "Details");
        Application.DoEvents();
        Assert.Equal(3, list.VirtualListSize); // the list made early shows the rows like one made on first show
    });

    [Fact]
    public void Panes_repaint_only_while_visible() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        var pane = rig.Form.PerformanceView.VisiblePane;

        SelectTab(rig.Form, "Details");
        Application.DoEvents();
        var paints = pane.RepaintCount;
        FireSamples(5);
        Assert.Equal(paints, pane.RepaintCount);

        SelectTab(rig.Form, "Performance");
        Application.DoEvents();
        Assert.True(pane.RepaintCount > paints, "selecting the tab did not repaint the pane"); // showing a window off-screen paints nothing
        paints = pane.RepaintCount;
        FireSamples(1);
        Assert.True(pane.RepaintCount > paints, "a sample did not repaint the visible pane");

        rig.Form.MinimizeWithoutActivation();
        paints = pane.RepaintCount;
        FireSamples(5);
        Assert.Equal(paints, pane.RepaintCount);

        rig.Form.RestoreWithoutActivation();
        Assert.True(pane.RepaintCount > paints, "restoring did not repaint the pane");

        void FireSamples(int count)
        {
            for (var i = 0; i < count; i++)
            {
                rig.Timer.Fire();
                Application.DoEvents();
            }
        }
    });

    [Fact]
    public void Paused_reads_no_devices_and_refresh_now_reads_once() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        var cpu = rig.Devices.FakeCpu();
        var reads = cpu.Reads;

        rig.Form.Command(CommandIds.SpeedPaused);
        rig.Timer.Fire();
        rig.Timer.Fire();
        Assert.Equal(reads, cpu.Reads);

        Assert.True(rig.Form.PressKey(Keys.F5));
        Assert.Equal(reads + 1, cpu.Reads);
    });

    [Fact]
    public void Device_change_message_rescans_on_the_next_sample() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        var disks = rig.Devices.FakeDisks();

        foreach (var change in new[] { DbtDevNodesChanged, DbtDeviceArrival, DbtDeviceRemoveComplete })
        {
            var enumerations = disks.Enumerations;

            User32.SendMessage(rig.Form.Handle, WmDeviceChange, change, 0);
            Assert.Equal(enumerations, disks.Enumerations);

            rig.Timer.Fire();
            Assert.Equal(enumerations + 1, disks.Enumerations);
        }
    });

    [Fact]
    public void Always_on_top_command_toggles_its_check_mark_and_z_order_without_activation() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        Assert.Equal(0, rig.Form.ZOrderChangesAtCreation); // off: no topmost move at creation
        Assert.False(Checked(rig.Form, CommandIds.AlwaysOnTop));
        var changes = rig.Form.ZOrderChanges.Count;

        rig.Form.Command(CommandIds.AlwaysOnTop);
        Assert.True(rig.Form.ZOrderChanges.Count > changes);
        Assert.True(Checked(rig.Form, CommandIds.AlwaysOnTop));
        changes = rig.Form.ZOrderChanges.Count;

        rig.Form.Command(CommandIds.AlwaysOnTop);
        Assert.True(rig.Form.ZOrderChanges.Count > changes);
        Assert.False(Checked(rig.Form, CommandIds.AlwaysOnTop));
        Assert.All(rig.Form.ZOrderChanges, flags => Assert.Equal(SwpNoActivate, flags & SwpNoActivate));
        Assert.False(rig.Form.TopMost); // the WinForms property stays unused: its setter would activate the window
    });

    [Fact]
    public void Exited_process_releases_icon_while_minimized() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        SelectTab(rig.Form, "Details");
        rig.Form.MinimizeWithoutActivation();
        var live = rig.Form.Icons.LiveCount;

        rig.Snapshots.Processes.RemoveAll(p => p.Pid == 2);
        rig.Form.Clock.RefreshNow();

        Assert.Equal(live - 1, rig.Form.Icons.LiveCount);
        Assert.Equal(2, rig.Form.Table.Count);
        Assert.Equal(3, rig.Form.Details.DisplayedRows.Count); // no view work while minimized
    });

    [Fact]
    public void Restoring_from_minimized_refreshes_rows_without_a_new_snapshot() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        SelectTab(rig.Form, "Details");
        rig.Form.MinimizeWithoutActivation();
        rig.Snapshots.Processes.RemoveAll(p => p.Pid == 2);
        rig.Form.Clock.RefreshNow();
        var reads = rig.Snapshots.Reads;

        rig.Form.RestoreWithoutActivation();

        Assert.Equal([1, 3], rig.Form.Details.DisplayedRows.Select(r => r.Id.Pid).Order());
        Assert.Equal(reads, rig.Snapshots.Reads);
    });

    [Fact]
    public void Restoring_from_minimized_keeps_the_window_size() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        var window = rig.Form.WindowRect;
        var client = rig.Form.ClientSize;
        var resizes = 0;
        rig.Form.PerformanceView.Resize += (_, _) => resizes++;

        for (var i = 0; i < 3; i++)
        {
            rig.Form.MinimizeWithoutActivation();
            resizes = 0;
            rig.Form.RestoreWithoutActivation();
            Assert.Equal(1, resizes); // laid out once, back at its size
        }

        Assert.Equal(window, rig.Form.WindowRect);
        Assert.Equal(client, rig.Form.ClientSize);
    });

    [Fact]
    public void Restoring_takes_the_place_windows_restores_to_when_it_moved_while_minimized() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        var moved = rig.Form.WindowRect;
        moved.Offset(100, 100);
        moved.Size += new Size(60, 40);
        var resizes = 0;
        rig.Form.PerformanceView.Resize += (_, _) => resizes++;

        rig.Form.MinimizeWithoutActivation();
        rig.Form.MoveNormalPositionWhileMinimized(moved);
        resizes = 0;
        rig.Form.RestoreWithoutActivation();

        Assert.Equal(moved, rig.Form.WindowRect);
        Assert.Equal(1, resizes);
    });

    [Fact]
    public void Failed_snapshot_keeps_the_last_rows() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        SelectTab(rig.Form, "Details");
        var cpuReads = rig.Devices.FakeCpu().Reads;

        rig.Snapshots.Fail = true;
        rig.Snapshots.Processes.Clear();
        rig.Form.Clock.RefreshNow();

        Assert.Equal(2, rig.Snapshots.Reads);
        Assert.Equal(3, rig.Form.Table.Count);
        Assert.Equal(3, rig.Form.Details.DisplayedRows.Count);
        Assert.Equal(3, rig.Form.Icons.LiveCount);
        Assert.Equal(cpuReads, rig.Devices.FakeCpu().Reads); // the whole tick is skipped, devices included

        rig.Snapshots.Fail = false;
        rig.Form.Clock.RefreshNow();
        Assert.Equal(0, rig.Form.Table.Count);
        Assert.Empty(rig.Form.Details.DisplayedRows);
        Assert.Equal(cpuReads + 1, rig.Devices.FakeCpu().Reads);
    });

    [Fact]
    public void Closing_saves_settings() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        rig.Form.Command(CommandIds.SpeedLow);
        rig.Form.Command(CommandIds.AlwaysOnTop);
        var bounds = rig.Form.WindowRect;

        rig.Form.Close();

        var loaded = new SettingsStore(SettingsPath).Load();
        Assert.Equal(UpdateSpeed.Low, loaded.UpdateSpeed);
        Assert.True(loaded.AlwaysOnTop);
        Assert.Equal(bounds, new Rectangle(loaded.WindowX!.Value, loaded.WindowY!.Value, loaded.WindowWidth, loaded.WindowHeight));
        Assert.False(loaded.Maximized);
    });

    [Fact]
    public void Closing_while_minimized_saves_the_normal_bounds() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        var normal = rig.Form.WindowRect;
        rig.Form.MinimizeWithoutActivation();

        rig.Form.Close();

        var loaded = new SettingsStore(SettingsPath).Load();
        Assert.Equal(normal, new Rectangle(loaded.WindowX!.Value, loaded.WindowY!.Value, loaded.WindowWidth, loaded.WindowHeight));
        Assert.False(loaded.Maximized);
    });

    [Fact]
    public void Column_change_is_saved_two_seconds_later() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        Application.DoEvents();
        Assert.False(File.Exists(SettingsPath)); // nothing saved yet

        var clock = Stopwatch.StartNew();
        rig.Form.Details.SortBy(DetailsColumn.Pid);
        Assert.False(File.Exists(SettingsPath));
        while (!File.Exists(SettingsPath) && clock.ElapsedMilliseconds < 6000)
        {
            Thread.Sleep(20);
            Application.DoEvents();
        }

        Assert.True(File.Exists(SettingsPath), "the settings were not saved within 6 s");
        Assert.InRange(clock.ElapsedMilliseconds, 1900, 6000);
        Assert.Equal(DetailsColumn.Pid, new SettingsStore(SettingsPath).Load().SortColumn);
    });

    [Fact]
    public void Every_icon_is_destroyed_and_dispose_destroys_the_menu() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath); // disposed again at the end if an assertion fails first
        Assert.Equal(1, rig.Icons.Destroyed); // the generic icon, straight after the image list copied it
        Assert.Equal(0, rig.Icons.Outstanding);

        rig.Form.ShowOffscreen();
        Assert.Equal(3, rig.Icons.Loaded);
        Assert.Equal(0, rig.Icons.Outstanding);
        var bar = GetMenu(rig.Form.Handle);
        Assert.True(IsMenu(bar));

        rig.Dispose();

        Assert.False(IsMenu(bar));
        Assert.Equal(0, rig.Icons.Outstanding);
        Assert.Equal(4, rig.Icons.Destroyed);
    });

    [Fact]
    public void Menu_stays_attached_across_handle_recreation() => Sta.Run(() =>
    {
        using var rig = new Rig(SettingsPath);
        rig.Form.ShowOffscreen();
        var bar = GetMenu(rig.Form.Handle);
        var oldHandle = rig.Form.Handle;

        rig.Form.Recreate();

        Assert.NotEqual(oldHandle, rig.Form.Handle);
        Assert.True(IsMenu(bar)); // detached before the old window was destroyed, so Windows did not destroy it
        Assert.Equal(bar, GetMenu(rig.Form.Handle));
    });

    /// <summary>The check mark of the main menu item <paramref name="id"/>, as the menu would show it now.</summary>
    private static bool Checked(OffscreenMainForm form, int id)
    {
        var bar = GetMenu(form.Handle);
        for (var i = 0; i < GetMenuItemCount(bar); i++) User32.SendMessage(form.Handle, WmInitMenuPopup, GetSubMenu(bar, i), i);
        return (GetMenuState(bar, (uint)id, 0) & MfChecked) != 0;
    }

    private static TabControl Tabs(MainForm form) => form.Controls.OfType<TabControl>().Single();

    /// <summary>Selects a tab as test mode's scenario does: setting <see cref="TabControl.SelectedTab"/> would activate the window.</summary>
    private static void SelectTab(MainForm form, string text)
    {
        var tabs = (MainForm.TabStrip)Tabs(form);
        tabs.ShowPage(tabs.TabPages.Cast<TabPage>().Single(p => p.Text == text));
    }

    private static int Scale(int logical, int dpi) => (int)Math.Round(logical * dpi / 96.0, MidpointRounding.AwayFromZero);

    private static unsafe string[] Texts(nint menu)
    {
        var count = GetMenuItemCount(menu);
        var texts = new string[count];
        var buffer = stackalloc char[64];
        for (var i = 0; i < count; i++)
        {
            var length = GetMenuString(menu, (uint)i, buffer, 64, MfByPosition);
            texts[i] = new string(buffer, 0, length);
        }

        return texts;
    }

    private static int[] Ids(nint menu) => [.. Enumerable.Range(0, GetMenuItemCount(menu)).Select(i => (int)GetMenuItemID(menu, i))];

    private static bool[] Checks(nint menu) =>
        [.. Enumerable.Range(0, GetMenuItemCount(menu)).Select(i => (GetMenuState(menu, (uint)i, MfByPosition) & MfChecked) != 0)];

    /// <summary>Snapshot source over a list the test edits. <see cref="Read"/> copies it, or throws while <see cref="Fail"/>.</summary>
    private sealed class FakeSnapshots : IProcessSnapshotSource
    {
        public readonly List<RawProcess> Processes = [P(1, "zeta.exe"), P(2, "alpha.exe"), P(3, "mid.exe")];
        public int Reads;
        public bool Fail;

        public void Read(List<RawProcess> into)
        {
            Reads++;
            into.Clear();
            if (Fail)
            {
                into.Add(P(99, "half.exe")); // a failing read may leave the list half-filled
                throw new InvalidOperationException("snapshot failed");
            }

            into.AddRange(Processes);
        }

        private static RawProcess P(int pid, string name) => new(pid, 0, 100 + pid, name, 0, 0, 0, 0, 1, 1, 1, 8, false);
    }

    /// <summary>Hands out real generic icons, so the image list accepts them, and checks that each is destroyed exactly once.</summary>
    private sealed class FakeIconLoader : IIconLoader
    {
        private readonly HashSet<nint> _outstanding = [];

        public int Loaded { get; private set; }
        public int Destroyed { get; private set; }
        public int Outstanding => _outstanding.Count;

        public nint CreateGeneric()
        {
            var icon = ShellIconLoader.LoadGenericIcon();
            Assert.NotEqual(0, icon);
            _outstanding.Add(icon);
            return icon;
        }

        public nint LoadSmallIcon(string path)
        {
            Loaded++;
            return CreateGeneric();
        }

        public void DestroyIcon(nint hIcon)
        {
            Assert.True(_outstanding.Remove(hIcon), "an icon was destroyed twice or was never handed out");
            Assert.True(User32.DestroyIcon(hIcon));
            Destroyed++;
        }
    }

    private sealed class Rig : IDisposable
    {
        public readonly FakeSnapshots Snapshots = new();
        public readonly FakeIconLoader Icons = new();
        public readonly FakeTimer Timer = new();
        public readonly DeviceSources Devices = FakeDeviceSources.Create();
        public readonly OffscreenMainForm Form;

        public Rig(string settingsPath, LaunchOptions? options = null)
        {
            var services = new AppServices(Snapshots, new FakeInfo(), Icons, Icons.CreateGeneric(), Timer, new SettingsStore(settingsPath), options ?? new LaunchOptions(null, null), Devices);
            Form = new OffscreenMainForm(services);
        }

        /// <summary>Checks the window one last time, unless the test closed it, then disposes it.</summary>
        public void Dispose()
        {
            try
            {
                if (Form.IsHandleCreated && !Form.IsDisposed) Form.AssertOffscreenAndInactive();
            }
            finally
            {
                Form.Dispose();
            }
        }
    }
}
