using System.Diagnostics;
using Tmcs.Details;
using Tmcs.Devices;
using Tmcs.Diagnostics;
using Tmcs.Icons;
using Tmcs.Native;
using Tmcs.Performance;
using Tmcs.Performance.Views;
using Tmcs.Processes;
using Tmcs.Sampling;
using Tmcs.Settings;
using Tmcs.Ui;

namespace Tmcs;

/// <summary>
/// The main window: the native menu bar, the Performance and Details tabs, and the wiring between the sample clock, the process
/// table, the icon store, the performance model and the two views. Every sample applies a snapshot to the table and reads every
/// device, whatever is showing, so an exited process gives back its icon on the same tick and the graphs never miss a point. Each
/// view is refreshed only while it can be seen, and at once when it becomes visible again. tmcs always opens on Performance.
/// In test mode (TMCS_DIAG set) it also runs the <see cref="TestHarness"/>, stays a background window that never takes focus or
/// rises above other windows, and never saves the settings.
/// </summary>
internal class MainForm : Form, IScenarioTarget
{
    private const int WmCommand = 0x0111;
    private const int WmInitMenuPopup = 0x0117;

    /// <summary>A change is saved this long after the last one, so a drag saves once.</summary>
    private const int SaveDelayMs = 2000;

    private readonly AppServices _services;
    private readonly AppSettings _settings;
    private readonly ProcessTable _table;
    private readonly PerformanceModel _performance;
    private readonly NativeImageList _images;
    private readonly IconStore _icons;
    private readonly HungWindowScanner _hungScanner = new();
    private readonly SampleClock _clock;
    private readonly TabStrip _tabs;
    private readonly TabPage _performancePage;
    private readonly PerformanceView _performanceView;
    private readonly TabPage _detailsPage;
    private readonly DetailsView _details;
    private readonly NativeMenu _menu;

    /// <summary>The Update speed submenu. The bar owns it.</summary>
    private readonly NativeMenu _speedMenu;

    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = SaveDelayMs };
    private readonly Icon? _appIcon;

    /// <summary>The diagnostics log and scenario. Test mode only.</summary>
    private readonly TestHarness? _harness;

    // Reused by every sample.
    private readonly List<RawProcess> _snapshot = [];
    private readonly HashSet<int> _hungPids = [];

    /// <summary>Snapshot times count from here. QPC counts from boot, and its ticks scaled to 100 ns would overflow a long after about a day of uptime.</summary>
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    /// <summary>False until <see cref="OnLoad"/> has placed the window, so the moves and resizes of startup schedule no save.</summary>
    private bool _loaded;

    private bool _wasMinimized;

    public MainForm(AppServices services)
    {
        _services = services;
        _settings = services.Store.Load();
        if (TestMode)
        {
            // A normal window at the saved bounds, never on top, sampling at Normal whatever was saved, so every soak runs alike.
            // Test mode never saves, so these stay in memory.
            _settings.AlwaysOnTop = false;
            _settings.Maximized = false;
            _settings.UpdateSpeed = UpdateSpeed.Normal;
        }

        // Text and MinimumSize are virtual. Setting them through base skips any override in a subclass not yet constructed.
        base.Text = "tmcs";
        _appIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        Icon = _appIcon;
        base.MinimumSize = LogicalToDeviceUnits(new Size(400, 300));
        Size = new Size(_settings.WindowWidth, _settings.WindowHeight); // at least MinimumSize; the position is applied in OnLoad

        var iconSize = User32.GetSystemMetricsForDpi(User32.SmCxSmIcon, (uint)DeviceDpi);
        _images = new NativeImageList(iconSize > 0 ? iconSize : 16);
        try
        {
            _icons = new IconStore(_images, services.Icons, services.GenericIcon);
        }
        finally
        {
            services.Icons.DestroyIcon(services.GenericIcon); // the image list keeps its own copy
        }

        _table = new ProcessTable(services.Info, CpuMath.MachineLogicalProcessors());
        _table.Added += row => row.IconSlot = _icons.Acquire(row.Info.ImagePath);
        _table.Removed += row => _icons.Release(row.Info.ImagePath);

        _performance = new PerformanceModel(services.Devices);

        _performanceView = new PerformanceView(_performance, _settings) { Dock = DockStyle.Fill };
        _performanceView.SettingsChanged += (_, _) => ScheduleSave();
        _performancePage = new TabPage("Performance");
        _performancePage.Controls.Add(_performanceView);
        _details = new DetailsView(_table, _images, _settings) { Dock = DockStyle.Fill };
        _details.SettingsChanged += (_, _) => ScheduleSave();
        _detailsPage = new TabPage("Details");
        _detailsPage.Controls.Add(_details);
        _tabs = new TabStrip { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(_performancePage); // the first page is selected: tmcs opens on Performance, and never saves the tab
        _tabs.TabPages.Add(_detailsPage);
        _tabs.SelectedIndexChanged += (_, _) => RefreshShowingView();
        Controls.Add(_tabs);

        (_menu, _speedMenu) = CreateMenu();

        _saveTimer.Tick += (_, _) => SaveSettings();
        _clock = new SampleClock(services.Timer) { Speed = _settings.UpdateSpeed };
        _clock.Sample += OnSample;

        if (TestMode) _harness = new TestHarness(this, services.Info, services.Options.DiagPath!, services.Options.Scenario);
    }

    internal ProcessTable Table => _table;

    internal PerformanceModel Performance => _performance;

    internal IconStore Icons => _icons;

    internal SampleClock Clock => _clock;

    internal DetailsView Details => _details;

    internal PerformanceView PerformanceView => _performanceView;

    internal TestHarness? Harness => _harness;

    /// <summary>
    /// The working areas a saved window position must reach, the primary screen's first: a position that reaches none of them is
    /// centred on the first. Tests return an off-screen area, so the form never moves onto a real screen.
    /// </summary>
    protected virtual IReadOnlyList<Rectangle> WorkingAreas()
    {
        var screens = Screen.AllScreens;
        var areas = new List<Rectangle>(screens.Length);
        foreach (var screen in screens)
            if (screen.Primary)
                areas.Add(screen.WorkingArea);
        foreach (var screen in screens)
            if (!screen.Primary)
                areas.Add(screen.WorkingArea);
        return areas;
    }

    private bool DetailsShowing => _tabs.SelectedTab == _detailsPage && WindowState != FormWindowState.Minimized;

    private bool PerformanceShowing => _tabs.SelectedTab == _performancePage && WindowState != FormWindowState.Minimized;

    private bool TestMode => _services.Options.IsTestMode;

    /// <summary>Test mode shows the window without activating it: it runs behind the owner's windows and never takes focus.</summary>
    protected override bool ShowWithoutActivation => TestMode;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        User32.SetMenu(Handle, _menu.Handle);
        if (TestMode) SinkToBottom(); // before it first shows, so it never appears above another window
        else if (_settings.AlwaysOnTop) ApplyAlwaysOnTop();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        // Windows destroys the menu attached to a window with the window. This one is destroyed in Dispose, and a recreated
        // window attaches it again, so it must not go with the window.
        User32.SetMenu(Handle, 0);
        base.OnHandleDestroyed(e);
    }

    /// <summary>Runs before the window first shows: restores the saved position (on first run Windows picks one), then takes the first sample.</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_settings.WindowX is { } x && _settings.WindowY is { } y)
            Bounds = WindowPlacement.Clamp(new Rectangle(x, y, Width, Height), WorkingAreas());
        if (_settings.Maximized) WindowState = FormWindowState.Maximized;
        _loaded = true;
        _clock.RefreshNow();
    }

    /// <summary>Test mode: once shown without activation, the window goes to the bottom of the z-order, then the scenario starts.</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_harness is null) return;
        SinkToBottom();
        _harness.Start();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        if (_loaded) ScheduleSave();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var minimized = WindowState == FormWindowState.Minimized;
        if (_wasMinimized && !minimized) RefreshShowingView(); // restored: show the current data at once
        _wasMinimized = minimized;
        if (!_loaded) return;
        if (!minimized) _settings.Maximized = WindowState == FormWindowState.Maximized; // minimized keeps what it restores to
        ScheduleSave();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!e.Cancel) SaveSettings();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            _clock.RefreshNow();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            // From the menu: no control handle, notification code 0.
            case WmCommand when m.LParam == 0 && HiWord(m.WParam) == 0:
                if (OnCommand(LoWord(m.WParam))) return;
                break;
            // Not for the window (system) menu.
            case WmInitMenuPopup when HiWord(m.LParam) == 0:
                UpdateMenuChecks();
                break;
            // A device came or went: the disks and GPUs are listed again at the next sample. DefWndProc still answers it.
            case User32.WmDeviceChange when m.WParam is User32.DbtDevNodesChanged or User32.DbtDeviceArrival or User32.DbtDeviceRemoveComplete:
                _performance.MarkDevicesChanged();
                break;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _saveTimer.Dispose();
            (_services.Timer as IDisposable)?.Dispose();
            _harness?.Dispose();
            _performance.Dispose(); // after the timer, so no sample reads a disposed source
        }

        // Destroys the window, which detaches the menu, and the list view, which only borrows the image list.
        base.Dispose(disposing);

        if (disposing)
        {
            _menu.Dispose();
            _images.Dispose();
            _appIcon?.Dispose();
        }
    }

    /// <summary>
    /// A scenario step, done synchronously so the runner's stopwatch covers what a user would wait for: the restore with its view
    /// step, the tab and pane switch, and the speed change. Unpaused is always Normal. A Performance step ends by painting the
    /// view, so the stopwatch covers the repaint too. The window is never activated, and after a restore it is sunk again, since
    /// restoring can raise it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The sidebar lists no device of the step's kind: no adapter, or no GPU. The soak
    /// must not hold another pane in its place, and test mode ends on it.</exception>
    void IScenarioTarget.Apply(ScenarioAction a)
    {
        if (a == ScenarioAction.Minimized)
        {
            _clock.Speed = UpdateSpeed.Normal;
            if (!User32.IsIconic(Handle)) ShowMinimizedWithoutActivation();
            return;
        }

        if (User32.IsIconic(Handle))
        {
            SinkToBottom();
            User32.ShowWindow(Handle, User32.SwShowNoActivate); // WM_SIZE arrives before this returns: OnResize runs the view step
            SinkToBottom();
        }

        if (a == ScenarioAction.Details)
        {
            _tabs.ShowPage(_detailsPage);
            _clock.Speed = UpdateSpeed.Normal;
            return;
        }

        // Paused pauses on the CPU pane: the Performance tab is where a view could keep working while paused.
        var kind = a switch
        {
            ScenarioAction.Cpu or ScenarioAction.Paused => DeviceKind.Cpu,
            ScenarioAction.Network => DeviceKind.Network,
            ScenarioAction.Gpu => DeviceKind.Gpu,
            _ => throw new ArgumentOutOfRangeException(nameof(a), a, null),
        };
        _tabs.ShowPage(_performancePage);
        _clock.Speed = a == ScenarioAction.Paused ? UpdateSpeed.Paused : UpdateSpeed.Normal;
        // The first listed of its kind: hidden adapters are not listed, and GPUs are listed from GPU 0.
        if (!_performanceView.Sidebar.SelectFirst(kind))
            throw new InvalidOperationException($"The scenario step {ScenarioParser.Name(a)} needs a {kind} device, and the sidebar lists none.");
        _performanceView.Update();
    }

    /// <summary>Minimizes the window without activating it. Tests pin the minimized position off-screen.</summary>
    protected virtual void ShowMinimizedWithoutActivation() => User32.ShowWindow(Handle, User32.SwShowMinNoActive);

    private static (NativeMenu Bar, NativeMenu Speed) CreateMenu()
    {
        var speed = NativeMenu.Popup(
            ("&High", CommandIds.SpeedHigh), ("&Normal", CommandIds.SpeedNormal), ("&Low", CommandIds.SpeedLow), ("&Paused", CommandIds.SpeedPaused));
        var bar = NativeMenu.Bar()
            .AddPopup("&File", NativeMenu.Popup(("E&xit", CommandIds.Exit)))
            .AddPopup("&Options", NativeMenu.Popup(("&Always on top", CommandIds.AlwaysOnTop)))
            .AddPopup("&View", NativeMenu.Popup(("&Refresh now", CommandIds.RefreshNow)).AddPopup("&Update speed", speed));
        return (bar, speed);
    }

    private static int LoWord(nint value) => (int)(value & 0xFFFF);

    private static int HiWord(nint value) => (int)((value >> 16) & 0xFFFF);

    /// <summary>
    /// One sample: read a snapshot, apply it to the table, read every device at the same time stamp, and refresh the view that can
    /// be seen.
    /// </summary>
    private void OnSample()
    {
        try
        {
            _services.Snapshots.Read(_snapshot);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            // A failed read skips the whole tick, devices included: the list may be half-filled, so the table keeps the last
            // snapshot's rows, and every graph misses the same point rather than half a tick being taken.
            return;
        }

        var t = Stopwatch.GetElapsedTime(_startTimestamp).Ticks; // TimeSpan ticks are 100 ns
        _table.Apply(_snapshot, t);
        _performance.Sample(t, _snapshot);
        RefreshShowingView();
    }

    /// <summary>Refreshes the view that can be seen, if any.</summary>
    private void RefreshShowingView()
    {
        if (DetailsShowing) RefreshView();
        else if (PerformanceShowing) _performanceView.RefreshVisible();
    }

    /// <summary>Shows the current table. Apply resets every row's status, so the hung scan comes first.</summary>
    private void RefreshView()
    {
        _hungScanner.Scan(_hungPids);
        _table.ApplyHung(_hungPids);
        _details.RefreshRows();
    }

    /// <returns>False for an id that is not a main menu command.</returns>
    private bool OnCommand(int id)
    {
        switch (id)
        {
            case CommandIds.Exit:
                Close();
                return true;
            case CommandIds.AlwaysOnTop:
                _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
                ApplyAlwaysOnTop();
                ScheduleSave();
                return true;
            case CommandIds.RefreshNow:
                _clock.RefreshNow();
                return true;
        }

        if (!CommandIds.TryGetSpeed(id, out var speed)) return false;
        _clock.Speed = speed;
        _settings.UpdateSpeed = speed;
        ScheduleSave();
        return true;
    }

    /// <summary>
    /// Moves the window into or out of the topmost band, per the settings, without activating it. Not through
    /// <see cref="Form.TopMost"/>: its setter moves the window without SWP_NOACTIVATE, and showing a form whose TopMost is set
    /// focuses a control, so either would activate the window. That property therefore stays false.
    /// </summary>
    private void ApplyAlwaysOnTop() =>
        User32.SetWindowPos(Handle, _settings.AlwaysOnTop ? User32.HwndTopmost : User32.HwndNoTopmost, 0, 0, 0, 0,
            User32.SwpNoMove | User32.SwpNoSize | User32.SwpNoActivate);

    /// <summary>Moves the window to the bottom of the z-order without activating it.</summary>
    private void SinkToBottom() =>
        User32.SetWindowPos(Handle, User32.HwndBottom, 0, 0, 0, 0, User32.SwpNoActivate | User32.SwpNoMove | User32.SwpNoSize);

    private void UpdateMenuChecks()
    {
        _speedMenu.CheckRadio(CommandIds.SpeedHigh, CommandIds.SpeedPaused, CommandIds.ForSpeed(_clock.Speed));
        _menu.SetChecked(CommandIds.AlwaysOnTop, _settings.AlwaysOnTop);
    }

    /// <summary>Restarts the one-shot save timer. Test mode never saves.</summary>
    private void ScheduleSave()
    {
        if (TestMode) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Saves the settings with the window's normal bounds. A failed write is dropped: the next change or the exit tries again.
    /// Test mode never saves: the soak runs on the owner's real settings file.
    /// </summary>
    private void SaveSettings()
    {
        _saveTimer.Stop();
        if (TestMode) return;
        var normal = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowX = normal.X;
        _settings.WindowY = normal.Y;
        _settings.WindowWidth = normal.Width;
        _settings.WindowHeight = normal.Height;
        _settings.Normalize();
        try
        {
            _services.Store.Save(_settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(e);
        }
    }

    /// <summary>
    /// The tab strip. <see cref="ShowPage"/> selects a page from code without moving the focus: selecting through
    /// <see cref="TabControl.SelectedTab"/> focuses the page's first control, and focusing a control of an inactive window
    /// activates the window, which test mode's must never be. It raises only SelectedIndexChanged, not Selecting, Selected or
    /// Deselecting, which nothing here subscribes to.
    /// </summary>
    internal sealed class TabStrip : TabControl
    {
        private const uint TcmSetCurSel = 0x1300 + 12;

        public void ShowPage(TabPage page)
        {
            var index = TabPages.IndexOf(page);
            if (index < 0 || index == SelectedIndex) return;
            if (!IsHandleCreated)
            {
                SelectedIndex = index; // only recorded until the handle exists
                return;
            }

            User32.SendMessage(Handle, TcmSetCurSel, index, 0);
            OnSelectedIndexChanged(EventArgs.Empty); // shows the page and raises SelectedIndexChanged, without the focus move
        }
    }
}
