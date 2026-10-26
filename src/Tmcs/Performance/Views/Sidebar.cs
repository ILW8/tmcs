using System.ComponentModel;
using Tmcs.Devices;
using Tmcs.Devices.Cpu;
using Tmcs.Devices.Disk;
using Tmcs.Devices.Gpu;
using Tmcs.Devices.Memory;
using Tmcs.Devices.Network;
using Tmcs.Performance.Graphs;
using Tmcs.Settings;
using Tmcs.Ui;

namespace Tmcs.Performance.Views;

/// <summary>
/// The Performance tab's sidebar: one owner-drawn entry per listed device, stacked from y 0 with no gaps, scrolled by the native
/// scrollbar. It owns the selection, which the view follows, and the menu that hides adapters.
/// <para>
/// Geometry is Task Manager's (look reference section 2) in 96-DPI units, scaled by the fonts' DPI. Painting creates no GDI or
/// USER object: the painters (one per kind), the selection brush and both menus are created once.
/// </para>
/// </summary>
internal sealed class Sidebar : ScrollableControl
{
    const int WmContextMenu = 0x007B;

    // Entry sizes: CPU and Memory are short; adapters and GPUs are wide, and the client edge clips them.
    const int ShortHeight = 60, TallHeight = 66;
    const int NarrowWidth = 194, WideWidth = 204;

    // From the entry's top left.
    const int GraphInset = 10, GraphWidth = 60;
    const int TitleX = 80, TitleY = 5;
    const int LineX = 81, Line1Y = 26, Line2Y = 41;

    /// <summary>Text is clipped here, with an end ellipsis.</summary>
    const int TextRight = 195;

    /// <summary>The gap between the two texts of a CPU or GPU sub line.</summary>
    const int PairGap = 5;

    /// <summary>
    /// The network line's segments sit in rectangles 1 px wider than their text (look reference section 2), so each starts 1 px
    /// after the one before ends. Capture 00's <c>S: 0 R: 0 Kbps</c> matches this column for column, and not without it.
    /// </summary>
    const int SegmentGap = 1;

    readonly PerformanceModel _model;
    readonly AppSettings _settings;
    readonly List<Device> _entries = [];

    /// <summary>The entries before a rebuild, held only while it runs.</summary>
    readonly List<Device> _previous = [];

    /// <summary>Indexed by <see cref="DeviceKind"/>.</summary>
    readonly GraphPainter[] _painters = new GraphPainter[Enum.GetValues<DeviceKind>().Length];

    readonly SolidBrush _selectedFill = new(Color.FromArgb(0xCD, 0xE8, 0xFF));

    /// <summary>The menu for an adapter (Hide, then Show hidden adapters), and the one for anywhere else.</summary>
    readonly NativeMenu _adapterMenu, _menu;

    PaneFonts _fonts;
    Device _selected;
    bool _showHidden;

    public Sidebar(PerformanceModel model, AppSettings settings, PaneFonts fonts)
    {
        _model = model;
        _settings = settings;
        _fonts = fonts;
        _selected = model.Cpu;
        // Selectable, so a click focuses it and Up and Down reach it.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        // These are virtual; set through base, as the pane does.
        base.BackColor = Color.White;
        base.AutoScroll = true;

        _painters[(int)DeviceKind.Cpu] = new GraphPainter(GraphPalettes.Cpu);
        _painters[(int)DeviceKind.Memory] = new GraphPainter(GraphPalettes.Memory);
        _painters[(int)DeviceKind.Disk] = new GraphPainter(GraphPalettes.Disk);
        _painters[(int)DeviceKind.Network] = new GraphPainter(GraphPalettes.Network);
        _painters[(int)DeviceKind.Gpu] = new GraphPainter(GraphPalettes.Gpu);

        const string showHidden = "&Show hidden adapters (0)"; // the count is set each time the menu opens
        _adapterMenu = NativeMenu.Popup(("&Hide", CommandIds.HideAdapter)).AddSeparator().AddItem(showHidden, CommandIds.ShowHiddenAdapters);
        _menu = NativeMenu.Popup((showHidden, CommandIds.ShowHiddenAdapters));

        _model.DevicesChanged += Rebuild;
        Rebuild();
    }

    /// <summary>The selected entry's device key. Starts at <c>cpu</c>.</summary>
    public string SelectedKey => _selected.Key;

    /// <summary>Raised when another entry is selected: by a click, a key, <see cref="SelectFirst"/>, or because the selected one left the list.</summary>
    public event Action? SelectionChanged;

    /// <summary>Raised after Hide changed <see cref="AppSettings.HiddenAdapters"/>.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>The view's fonts, replaced on a DPI change.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PaneFonts Fonts
    {
        get => _fonts;
        set
        {
            _fonts = value;
            UpdateScrollSize();
            Invalidate();
        }
    }

    /// <summary>The selected entry's device, always one of <see cref="Entries"/>.</summary>
    internal Device Selected => _selected;

    /// <summary>The model's devices in its order, without the hidden adapters unless they are shown.</summary>
    internal IReadOnlyList<Device> Entries => _entries;

    /// <summary>Selects the first listed entry of <paramref name="kind"/>.</summary>
    /// <returns>False when none is listed; the selection stays.</returns>
    public bool SelectFirst(DeviceKind kind)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Kind != kind) continue;
            SetSelected(_entries[i]);
            return true;
        }

        return false;
    }

    /// <summary>Selects the listed entry with <paramref name="key"/>, as a click on it would.</summary>
    /// <returns>False when none is listed; the selection stays.</returns>
    internal bool SelectKey(string key)
    {
        var index = IndexOf(key);
        if (index < 0) return false;
        SetSelected(_entries[index]);
        return true;
    }

    /// <summary>Entry <paramref name="index"/>'s rectangle in content coordinates, unclipped.</summary>
    internal Rectangle EntryBounds(int index)
    {
        var top = 0;
        for (var i = 0; i < index; i++) top += EntryHeight(_entries[i].Kind);
        var kind = _entries[index].Kind;
        return new Rectangle(0, top, EntryWidth(kind), EntryHeight(kind));
    }

    /// <summary>The index of the entry under <paramref name="client"/>, a point in client coordinates; -1 for none.</summary>
    internal int EntryAt(Point client)
    {
        var y = client.Y - AutoScrollPosition.Y;
        var top = 0;
        for (var i = 0; i < _entries.Count; i++)
        {
            var kind = _entries[i].Kind;
            var height = EntryHeight(kind);
            if (y >= top && y < top + height) return client.X >= 0 && client.X < EntryWidth(kind) ? i : -1;
            top += height;
        }

        return -1;
    }

    /// <summary>
    /// The menu for <paramref name="target"/>, the entry it opens on (null below the entries), with its text and state set for
    /// now. <see cref="NativeMenu.Track"/> shows it; tests read it.
    /// </summary>
    internal NativeMenu PrepareMenu(Device? target)
    {
        // N counts the hidden adapters the model lists: GUIDs of absent adapters don't count.
        var hidden = 0;
        var devices = _model.Devices;
        for (var i = 0; i < devices.Count; i++)
            if (devices[i] is NetworkDevice a && IsHidden(a)) hidden++;

        var adapter = target as NetworkDevice;
        var menu = adapter is null ? _menu : _adapterMenu;
        // SetText replaces the item, which clears its state, so the state is set after it.
        menu.SetText(CommandIds.ShowHiddenAdapters, $"&Show hidden adapters ({hidden})");
        menu.SetChecked(CommandIds.ShowHiddenAdapters, _showHidden);
        menu.SetEnabled(CommandIds.ShowHiddenAdapters, hidden > 0 || _showHidden);
        // A hidden adapter is only listed while they are shown; then the same item un-hides it.
        if (adapter is not null) menu.SetChecked(CommandIds.HideAdapter, IsHidden(adapter));
        return menu;
    }

    /// <summary>
    /// Carries out a menu command for <paramref name="target"/>, the entry the menu opened on. Tests call it directly, since a
    /// tracked menu would block.
    /// </summary>
    internal void OnCommand(int id, Device? target)
    {
        switch (id)
        {
            case CommandIds.HideAdapter when target is NetworkDevice adapter:
                var guid = adapter.Info.InterfaceGuid;
                if (_settings.HiddenAdapters.RemoveAll(h => Guid.TryParse(h, out var g) && g == guid) == 0)
                    _settings.HiddenAdapters.Add(guid.ToString("B"));
                Rebuild();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case CommandIds.ShowHiddenAdapters:
                // For the session only: hiding is remembered, showing is not.
                _showHidden = !_showHidden;
                Rebuild();
                break;
        }
    }

    /// <summary>The background and the entries that meet <paramref name="clip"/>, in client coordinates. Tests call it directly.</summary>
    internal void PaintTo(Graphics g, Rectangle clip)
    {
        g.FillRectangle(Brushes.White, clip);
        var top = AutoScrollPosition.Y;
        for (var i = 0; i < _entries.Count; i++)
        {
            var device = _entries[i];
            var entry = new Rectangle(0, top, EntryWidth(device.Kind), EntryHeight(device.Kind));
            if (entry.Top >= clip.Bottom) break;
            if (entry.IntersectsWith(clip)) PaintEntry(g, device, entry);
            top = entry.Bottom;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e) => PaintTo(e.Graphics, e.ClipRectangle);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        var index = EntryAt(e.Location);
        if (index >= 0) SetSelected(_entries[index]);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    /// <summary>Up and Down select the entry above and below and scroll it into view, as Task Manager's sidebar does.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.KeyData switch { Keys.Up => -1, Keys.Down => 1, _ => 0 };
        if (step == 0) return;
        var index = Math.Clamp(_entries.IndexOf(_selected) + step, 0, _entries.Count - 1);
        SetSelected(_entries[index]);
        ScrollIntoView(index);
        e.Handled = true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmContextMenu && TryShowMenu(m.LParam)) return;
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.DevicesChanged -= Rebuild;
            foreach (var painter in _painters) painter.Dispose();
            _selectedFill.Dispose();
            _adapterMenu.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }

    int EntryHeight(DeviceKind kind) => _fonts.Scale(kind is DeviceKind.Cpu or DeviceKind.Memory ? ShortHeight : TallHeight);

    int EntryWidth(DeviceKind kind) => _fonts.Scale(kind is DeviceKind.Network or DeviceKind.Gpu ? WideWidth : NarrowWidth);

    int IndexOf(string key)
    {
        for (var i = 0; i < _entries.Count; i++)
            if (_entries[i].Key == key) return i;
        return -1;
    }

    bool IsHidden(NetworkDevice adapter)
    {
        foreach (var entry in _settings.HiddenAdapters)
            if (Guid.TryParse(entry, out var guid) && guid == adapter.Info.InterfaceGuid) return true;
        return false;
    }

    /// <summary>
    /// Refills the entries from the model. It runs on every <see cref="PerformanceModel.DevicesChanged"/> and every hide or show,
    /// whatever is showing, so no entry outlives its device; when the selected device left the list, the selection moves on in
    /// the same call.
    /// </summary>
    void Rebuild()
    {
        _previous.Clear();
        _previous.AddRange(_entries);
        _entries.Clear();
        var devices = _model.Devices;
        for (var i = 0; i < devices.Count; i++)
        {
            if (_showHidden || devices[i] is not NetworkDevice adapter || !IsHidden(adapter)) _entries.Add(devices[i]);
        }

        var next = Successor();
        _previous.Clear();
        UpdateScrollSize();
        Invalidate();
        SetSelected(next);
    }

    /// <summary>
    /// The entry to select after a rebuild: the selected device if it is still listed; else an entry of the same kind with the
    /// same title and subtitle (a GPU whose LUID changed after a driver restart, a re-created adapter); else the nearest entry
    /// above it that is still listed, which is CPU at the latest.
    /// </summary>
    Device Successor()
    {
        var old = _selected;
        var index = IndexOf(old.Key);
        if (index >= 0) return _entries[index];

        foreach (var device in _entries)
            if (device.Kind == old.Kind && device.Title == old.Title && device.Subtitle == old.Subtitle) return device;

        for (var i = _previous.IndexOf(old) - 1; i >= 0; i--)
        {
            index = IndexOf(_previous[i].Key);
            if (index >= 0) return _entries[index];
        }

        return _model.Cpu;
    }

    void SetSelected(Device device)
    {
        if (ReferenceEquals(device, _selected)) return;
        _selected = device;
        Invalidate();
        SelectionChanged?.Invoke();
    }

    void UpdateScrollSize()
    {
        var height = 0;
        for (var i = 0; i < _entries.Count; i++) height += EntryHeight(_entries[i].Kind);
        AutoScrollMinSize = new Size(0, height);
    }

    void ScrollIntoView(int index)
    {
        var entry = EntryBounds(index);
        var top = -AutoScrollPosition.Y;
        if (entry.Top < top) AutoScrollPosition = new Point(0, entry.Top);
        else if (entry.Bottom > top + ClientSize.Height) AutoScrollPosition = new Point(0, entry.Bottom - ClientSize.Height);
    }

    /// <returns>False for a right click outside the client area: the scrollbar keeps its own menu.</returns>
    bool TryShowMenu(nint lParam)
    {
        Device? target;
        Point screen;
        var packed = unchecked((int)lParam);
        if (packed == -1)
        {
            // From the keyboard: the selected entry, with the menu at its top left, kept inside the client area.
            target = _selected;
            var entry = EntryBounds(_entries.IndexOf(_selected));
            screen = PointToScreen(new Point(entry.X, Math.Clamp(entry.Y + AutoScrollPosition.Y, 0, ClientSize.Height)));
        }
        else
        {
            // Signed words: screen coordinates are negative left of or above the primary monitor.
            screen = new Point(unchecked((short)packed), unchecked((short)(packed >> 16)));
            var client = PointToClient(screen);
            if (!ClientRectangle.Contains(client)) return false;
            var index = EntryAt(client);
            target = index < 0 ? null : _entries[index];
        }

        var command = PrepareMenu(target).Track(this, screen);
        if (command != 0) OnCommand(command, target);
        return true;
    }

    void PaintEntry(Graphics g, Device device, Rectangle entry)
    {
        var f = _fonts;
        // The selection fills the whole entry, with no border and no focus rectangle; the graph's interior stays white.
        g.FillRectangle(ReferenceEquals(device, _selected) ? _selectedFill : Brushes.White, entry);

        var inset = f.Scale(GraphInset);
        var graph = new Rectangle(entry.X + inset, entry.Y + inset, f.Scale(GraphWidth), entry.Height - f.Scale(2 * GraphInset));
        var right = entry.X + f.Scale(TextRight);
        var line1 = new Point(entry.X + f.Scale(LineX), entry.Y + f.Scale(Line1Y));
        var line2 = new Point(line1.X, entry.Y + f.Scale(Line2Y));
        Draw(g, device.Title, f.Subtitle, new Point(entry.X + f.Scale(TitleX), entry.Y + f.Scale(TitleY)), PaneText.Black, right);

        var painter = _painters[(int)device.Kind];
        var history = device.History;
        switch (device)
        {
            case CpuDevice cpu:
                painter.Paint(g, graph, history, 0, 100, 0); // no kernel series
                Pair(g, PerfFormat.Percent(cpu.ShownUtilization(_settings.LinuxStyleCpu)), PerfFormat.Ghz(cpu.SpeedMhz), line1, right);
                break;
            case MemoryDevice memory:
                painter.Paint(g, graph, history, 0, memory.Info.TotalPhysical, 0);
                Draw(g, memory.SidebarText, f.Label, line1, PaneText.Black, right);
                break;
            case DiskDevice disk:
                painter.Paint(g, graph, history, 0, 100, 0);
                Draw(g, disk.Info.Type, f.Label, line1, PaneText.Black, right);
                Draw(g, PerfFormat.Percent(disk.ActivePercent), f.Label, line2, PaneText.Black, right);
                break;
            case NetworkDevice adapter:
                var max = GraphScales.Network(Math.Max(history.MaxVisible(0), history.MaxVisible(1))).Max;
                painter.Paint(g, graph, history, 0, max, 0, second: 1);
                Draw(g, adapter.Info.Alias, f.Label, line1, PaneText.Black, right);
                Rates(g, adapter, line2, right);
                break;
            case GpuDevice gpu:
                painter.Paint(g, graph, history, 0, 100, 0);
                Draw(g, gpu.Info.Name, f.Label, line1, PaneText.Black, right);
                // The temperature's color was never captured: black (plan's call).
                Pair(g, PerfFormat.Percent(gpu.Counters.Utilization), gpu.TemperatureText, line2, right);
                break;
        }
    }

    /// <summary>Two texts on one line, the second 5 px after the end of the first; none without a second value.</summary>
    void Pair(Graphics g, string first, string? second, Point at, int right)
    {
        var font = _fonts.Label;
        Draw(g, first, font, at, PaneText.Black, right);
        if (second is null) return;
        var x = at.X + PaneText.Width(first, font) + _fonts.Scale(PairGap);
        Draw(g, second, font, new Point(x, at.Y), PaneText.Black, right);
    }

    /// <summary>
    /// <c>S: x R: y unit</c>: four segments, each with a trailing space and starting where the one before ends, plus
    /// <see cref="SegmentGap"/>. The labels are gray; only the receive value carries the unit.
    /// </summary>
    void Rates(Graphics g, NetworkDevice adapter, Point at, int right)
    {
        var (send, receive) = PerfFormat.SidebarRates(adapter.SendBitsPerSecond, adapter.ReceiveBitsPerSecond);
        var x = Segment(g, "S: ", at.X, at.Y, PaneText.Gray, right);
        x = Segment(g, send + " ", x, at.Y, PaneText.Black, right);
        x = Segment(g, "R: ", x, at.Y, PaneText.Gray, right);
        Segment(g, receive + " ", x, at.Y, PaneText.Black, right);
    }

    /// <returns>Where the next segment starts.</returns>
    int Segment(Graphics g, string text, int x, int y, Color color, int right)
    {
        var font = _fonts.Label;
        Draw(g, text, font, new Point(x, y), color, right);
        return x + PaneText.Width(text, font) + _fonts.Scale(SegmentGap);
    }

    /// <summary>Draws <paramref name="text"/> clipped at <paramref name="right"/> with an end ellipsis; nothing when it starts past it.</summary>
    static void Draw(Graphics g, string text, Font font, Point at, Color color, int right) =>
        PaneText.Draw(g, text, font, at, color, right - at.X);
}
