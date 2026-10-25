using Tmcs.Devices;
using Tmcs.Settings;

namespace Tmcs.Performance.Views;

/// <summary>
/// The Performance tab: the sidebar, a divider, and one pane per <see cref="DeviceKind"/>, all created at startup, of which only
/// the pane of the device selected in the sidebar shows. Geometry is Task Manager's (look reference section 1) with the origin
/// at its tab content, in 96-DPI units scaled by the fonts' DPI. The bottom 48 px stay white and empty: "Fewer details" and
/// "Open Resource Monitor" are out of scope, and keeping the band keeps the pane as tall as Task Manager's.
/// </summary>
internal sealed class PerformanceView : Control
{
    readonly Sidebar _sidebar;
    readonly PerformancePane[] _panes;
    readonly SolidBrush _divider = new(Color.FromArgb(0xD9, 0xD9, 0xD9));
    PaneFonts _fonts;
    PerformancePane _visible;

    /// <param name="model">The devices the sidebar lists and the panes show.</param>
    /// <param name="settings">Where the sidebar keeps the hidden adapters.</param>
    public PerformanceView(PerformanceModel model, AppSettings settings)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        _fonts = new PaneFonts(DeviceDpi);

        _sidebar = new Sidebar(model, settings, _fonts);
        _sidebar.SelectionChanged += ShowSelected;
        _sidebar.SettingsChanged += (_, e) => SettingsChanged?.Invoke(this, e);
        Controls.Add(_sidebar);

        _panes = new PerformancePane[Enum.GetValues<DeviceKind>().Length];
        for (var i = 0; i < _panes.Length; i++)
        {
            _panes[i] = (DeviceKind)i switch
            {
                DeviceKind.Cpu => new CpuPane(_fonts),
                DeviceKind.Memory => new MemoryPane(_fonts),
                DeviceKind.Disk => new DiskPane(_fonts),
                DeviceKind.Network => new NetworkPane(_fonts),
                _ => new TitlePane(_fonts),
            };
            _panes[i].Visible = false;
            Controls.Add(_panes[i]);
        }

        _visible = _panes[(int)DeviceKind.Cpu];
        ShowSelected();
    }

    /// <summary>Raised after the sidebar's Hide changed <see cref="AppSettings.HiddenAdapters"/>.</summary>
    public event EventHandler? SettingsChanged;

    public PaneFonts Fonts => _fonts;

    /// <summary>The device list, which owns the selection.</summary>
    internal Sidebar Sidebar => _sidebar;

    /// <summary>Where the sidebar goes: (4, 7, 214, height − 55).</summary>
    internal Rectangle SidebarSlot =>
        new(_fonts.Scale(4), _fonts.Scale(7), _fonts.Scale(214), Math.Max(0, ClientSize.Height - _fonts.Scale(55)));

    /// <summary>Indexed by <see cref="DeviceKind"/>.</summary>
    internal IReadOnlyList<PerformancePane> Panes => _panes;

    internal PerformancePane VisiblePane => _visible;

    /// <summary>
    /// Repaints what shows from the current histories: the sidebar and the visible pane, nothing else. The sample just taken can
    /// change the pane's content height, so its scroll range is brought up to date first, here and not in its paint.
    /// </summary>
    public void RefreshVisible()
    {
        _sidebar.Invalidate();
        _visible.UpdateScrollSize();
        _visible.Invalidate();
    }

    /// <summary>
    /// Replaces the fonts when <paramref name="dpi"/> differs from theirs: disposes the old ones, creates new ones, and lays out
    /// and repaints the sidebar and the panes at the new scale.
    /// </summary>
    internal void ApplyDpi(int dpi)
    {
        if (dpi == _fonts.Dpi) return;
        var old = _fonts;
        _fonts = new PaneFonts(dpi);
        _sidebar.Fonts = _fonts;
        foreach (var pane in _panes) pane.Fonts = _fonts;
        old.Dispose();
        LayOut();
        Invalidate(invalidateChildren: true);
    }

    /// <summary>The background and the divider; the sidebar and the panes paint themselves.</summary>
    internal void PaintTo(Graphics g)
    {
        var size = ClientSize;
        g.FillRectangle(Brushes.White, 0, 0, size.Width, size.Height);
        // A 1-px line at x 218, from y 17 to height − 53 inclusive.
        var top = _fonts.Scale(17);
        var bottom = size.Height - _fonts.Scale(53);
        if (bottom >= top) g.FillRectangle(_divider, _fonts.Scale(218), top, _fonts.Scale(1), bottom - top + 1);
    }

    protected override void RescaleConstantsForDpi(int deviceDpiOld, int deviceDpiNew)
    {
        base.RescaleConstantsForDpi(deviceDpiOld, deviceDpiNew);
        ApplyDpi(deviceDpiNew);
    }

    /// <summary>The window may be created on a monitor whose DPI differs from the one the fonts were made for.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpi(DeviceDpi);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayOut();
    }

    /// <summary><see cref="PaintTo"/> fills the background.</summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e) => PaintTo(e.Graphics);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing); // the sidebar and the panes first: they paint with the fonts
        if (!disposing) return;
        _fonts.Dispose();
        _divider.Dispose();
    }

    /// <summary>
    /// Shows the sidebar's selection in its kind's pane, on every selection change, including the one a rescan or a hide makes.
    /// The other panes let go of their device, so no pane keeps one the model dropped.
    /// </summary>
    void ShowSelected()
    {
        var device = _sidebar.Selected;
        var pane = _panes[(int)device.Kind];
        if (pane != _visible)
        {
            _visible.Visible = false;
            _visible.Device = null;
            _visible = pane;
        }

        pane.Device = device;
        pane.Visible = true;
    }

    /// <summary>The sidebar in its slot, and every pane at (226, 0, width − 226, height − 48).</summary>
    void LayOut()
    {
        _sidebar.Bounds = SidebarSlot;
        var size = ClientSize;
        var x = _fonts.Scale(226);
        var bounds = new Rectangle(x, 0, Math.Max(0, size.Width - x), Math.Max(0, size.Height - _fonts.Scale(48)));
        foreach (var pane in _panes) pane.Bounds = bounds;
    }
}
