using System.Diagnostics;
using Tmcs.Details;
using Tmcs.Icons;
using Tmcs.Native;
using Tmcs.Processes;
using Tmcs.Sampling;
using Tmcs.Settings;
using Tmcs.Ui;

namespace Tmcs;

/// <summary>
/// The main window: the native menu bar, the tabs (only Details so far) and the wiring between the sample clock, the process
/// table, the icon store and the Details view. Every sample applies a snapshot to the table, whatever is showing, so an exited
/// process gives back its icon on the same tick. The Details view is refreshed only while it can be seen, and at once when it
/// becomes visible again.
/// </summary>
internal class MainForm : Form
{
    private const int WmCommand = 0x0111;
    private const int WmInitMenuPopup = 0x0117;

    /// <summary>A change is saved this long after the last one, so a drag saves once.</summary>
    private const int SaveDelayMs = 2000;

    private readonly AppServices _services;
    private readonly AppSettings _settings;
    private readonly ProcessTable _table;
    private readonly NativeImageList _images;
    private readonly IconStore _icons;
    private readonly HungWindowScanner _hungScanner = new();
    private readonly SampleClock _clock;
    private readonly TabControl _tabs;
    private readonly TabPage _detailsPage;
    private readonly DetailsView _details;
    private readonly NativeMenu _menu;

    /// <summary>The Update speed submenu. The bar owns it.</summary>
    private readonly NativeMenu _speedMenu;

    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = SaveDelayMs };
    private readonly Icon? _appIcon;

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

        // Text and MinimumSize are virtual. The only subclass, the tests' off-screen form, overrides neither.
        // ReSharper disable VirtualMemberCallInConstructor
        Text = "tmcs";
        _appIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        Icon = _appIcon;
        MinimumSize = LogicalToDeviceUnits(new Size(400, 300));
        Size = new Size(_settings.WindowWidth, _settings.WindowHeight); // at least MinimumSize; the position is applied in OnLoad
        // ReSharper restore VirtualMemberCallInConstructor

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

        _table = new ProcessTable(services.Info, Environment.ProcessorCount);
        _table.Added += row => row.IconSlot = _icons.Acquire(row.Info.ImagePath);
        _table.Removed += row => _icons.Release(row.Info.ImagePath);

        _details = new DetailsView(_table, _images, _settings) { Dock = DockStyle.Fill };
        _details.SettingsChanged += (_, _) => ScheduleSave();
        _detailsPage = new TabPage("Details");
        _detailsPage.Controls.Add(_details);
        _tabs = new TabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(_detailsPage);
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (DetailsShowing) RefreshView();
        };
        Controls.Add(_tabs);

        (_menu, _speedMenu) = CreateMenu();

        _saveTimer.Tick += (_, _) => SaveSettings();
        _clock = new SampleClock(services.Timer) { Speed = _settings.UpdateSpeed };
        _clock.Sample += OnSample;
    }

    internal ProcessTable Table => _table;

    internal IconStore Icons => _icons;

    internal SampleClock Clock => _clock;

    internal DetailsView Details => _details;

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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        User32.SetMenu(Handle, _menu.Handle);
        if (_settings.AlwaysOnTop) ApplyAlwaysOnTop();
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

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        if (_loaded) ScheduleSave();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var minimized = WindowState == FormWindowState.Minimized;
        if (_wasMinimized && !minimized && DetailsShowing) RefreshView(); // restored: show the current table at once
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
            case WmCommand when m.LParam == 0 && HiWord(m.WParam) == 0 && OnCommand(LoWord(m.WParam)):
                return;
            // Not for the window (system) menu.
            case WmInitMenuPopup when HiWord(m.LParam) == 0:
                UpdateMenuChecks();
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

    /// <summary>One sample: read a snapshot, apply it to the table, and refresh the view if it can be seen.</summary>
    private void OnSample()
    {
        try
        {
            _services.Snapshots.Read(_snapshot);
        }
        catch (Exception)
        {
            return; // a failed read skips the tick: the list may be half-filled, so the table keeps the last snapshot's rows
        }

        _table.Apply(_snapshot, Stopwatch.GetElapsedTime(_startTimestamp).Ticks); // TimeSpan ticks are 100 ns
        if (DetailsShowing) RefreshView();
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

    private void UpdateMenuChecks()
    {
        _speedMenu.CheckRadio(CommandIds.SpeedHigh, CommandIds.SpeedPaused, CommandIds.ForSpeed(_clock.Speed));
        _menu.SetChecked(CommandIds.AlwaysOnTop, _settings.AlwaysOnTop);
    }

    /// <summary>Restarts the one-shot save timer.</summary>
    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Saves the settings with the window's normal bounds. A failed write is dropped: the next change or the exit tries again.</summary>
    private void SaveSettings()
    {
        _saveTimer.Stop();
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
        }
    }
}
