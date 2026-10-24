using System.ComponentModel;
using Tmcs.Devices;

namespace Tmcs.Performance.Views;

/// <summary>
/// The right-hand pane of the Performance tab: one per <see cref="DeviceKind"/>, reused for every device of that kind.
/// Owner-drawn and double-buffered on a white background. It paints the title and subtitle, then <see cref="PaintBody"/>.
/// <para>
/// Geometry is Task Manager's (look reference sections 1 and 7) in 96-DPI units, scaled by the fonts' DPI, and in content
/// coordinates: <see cref="PaintTo"/> applies the scroll offset as a transform. Only a pane whose <see cref="ContentHeight"/>
/// is taller than the pane shows the native scrollbar, which costs no window and no GDI object.
/// </para>
/// </summary>
internal abstract class PerformancePane : ScrollableControl
{
    /// <summary>Each legend marker dash is this tall, with as much space below it.</summary>
    const int Dash = 3;

    const int Dashes = 7;
    const int SolidMarkerHeight = 40;
    const int MarkerWidth = 2;

    /// <summary>The markers' color is set before each fill, so one brush serves both.</summary>
    readonly SolidBrush _marker = new(Color.Black);

    PaneFonts _fonts;
    Device? _device;

    protected PerformancePane(PaneFonts fonts)
    {
        _fonts = fonts;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        // These are virtual. Setting them through base skips any override in a subclass not yet constructed.
        base.BackColor = Color.White;
        base.AutoScroll = true;
    }

    /// <summary>The device shown, set by the view.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Device? Device
    {
        get => _device;
        set
        {
            if (ReferenceEquals(_device, value)) return;
            _device = value;
            Invalidate();
        }
    }

    /// <summary>The height the content needs, in 96-DPI units. Above the pane's height, the pane scrolls.</summary>
    public virtual int ContentHeight => 0;

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

    // The pane's content edges. They hide the control's own Left and Right, which a pane never needs: the view places it by
    // its Bounds.

    /// <summary>Graphs and the info block start here.</summary>
    public new int Left => _fonts.Scale(19);

    /// <summary>The content's right edge, exclusive. A scrollbar narrows the client area and so moves it, as in Task Manager.</summary>
    public new int Right => ClientSize.Width - _fonts.Scale(24);

    /// <summary>The top captions' y.</summary>
    public int CaptionY => _fonts.Scale(53);

    /// <summary>The first graph's top.</summary>
    public int GraphTop => _fonts.Scale(69);

    /// <summary>The info block ends here, 20 px above the bottom of the pane or of its scrolling content.</summary>
    public int InfoBottom => Math.Max(ClientSize.Height, _fonts.Scale(ContentHeight)) - _fonts.Scale(20);

    /// <summary>
    /// Repaints asked for since the pane was created: each is one paint on screen. Tests count them to check that hidden panes
    /// are left alone. Not counted in OnPaint: off-screen, where tests run, every update rectangle is empty, and WinForms skips
    /// OnPaint for those.
    /// </summary>
    internal int RepaintCount { get; private set; }

    /// <summary>The whole paint for the current <see cref="Control.ClientSize"/>. Tests call it directly: DrawToBitmap refuses an empty control.</summary>
    internal void PaintTo(Graphics g)
    {
        var size = ClientSize;
        g.FillRectangle(Brushes.White, 0, 0, size.Width, size.Height);
        var scroll = AutoScrollPosition;
        g.TranslateTransform(scroll.X, scroll.Y);
        try
        {
            if (_device is { } device)
            {
                PaneText.Draw(g, device.PaneTitle, _fonts.Title, new Point(_fonts.Scale(17), _fonts.Scale(10)), PaneText.Black);
                var subtitle = device.Subtitle;
                var x = Right - PaneText.Width(subtitle, _fonts.Subtitle);
                PaneText.Draw(g, subtitle, _fonts.Subtitle, new Point(x, _fonts.Scale(24)), PaneText.Black);
            }

            PaintBody(g);
        }
        finally
        {
            g.TranslateTransform(-scroll.X, -scroll.Y);
        }
    }

    /// <summary>Everything below the title, in content coordinates.</summary>
    protected abstract void PaintBody(Graphics g);

    /// <summary>
    /// A graph's captions, in the Caption font and gray: <paramref name="topLeft"/> and <paramref name="topRight"/> 16 px above
    /// the graph, and with <paramref name="bottom"/> <c>60 seconds</c> and <c>0</c> 2 px below it. Right captions end at the
    /// graph's right edge.
    /// </summary>
    protected void Captions(Graphics g, Rectangle graph, string topLeft, string topRight, bool bottom)
    {
        var font = _fonts.Caption;
        var top = graph.Y - _fonts.Scale(16);
        PaneText.Draw(g, topLeft, font, new Point(graph.X, top), PaneText.Gray);
        PaneText.Draw(g, topRight, font, new Point(graph.Right - PaneText.Width(topRight, font), top), PaneText.Gray);
        if (!bottom) return;

        var y = graph.Bottom + _fonts.Scale(2);
        PaneText.Draw(g, "60 seconds", font, new Point(graph.X, y), PaneText.Gray);
        PaneText.Draw(g, "0", font, new Point(graph.Right - PaneText.Width("0", font), y), PaneText.Gray);
    }

    /// <summary>
    /// The info block at (<see cref="Left"/>, <see cref="InfoTop"/>): labels in the Label font and gray, values 15 px below in
    /// the Value font and black, the legend markers in <paramref name="solid"/> and <paramref name="dashed"/>, and the table's
    /// keys (gray) and values (black) in the Label font.
    /// </summary>
    protected void PaintInfo(Graphics g, InfoLayout layout, Color solid, Color dashed)
    {
        var f = _fonts;
        var left = Left;
        var top = InfoTop(layout);
        foreach (var placed in layout.Items)
        {
            var item = placed.Item;
            var x = left + f.Scale(placed.X);
            var y = top + f.Scale(placed.Y);
            var textX = left + f.Scale(placed.TextX);
            PaneText.Draw(g, item.Label, f.Label, new Point(textX, y), PaneText.Gray);
            PaneText.Draw(g, item.Value, f.Value, new Point(textX, y + f.Scale(15)), PaneText.Black);

            // A marker starts 3 px below its label's top.
            if (item.Marker == InfoMarker.Solid)
            {
                _marker.Color = solid;
                g.FillRectangle(_marker, x, y + f.Scale(3), f.Scale(MarkerWidth), f.Scale(SolidMarkerHeight));
            }
            else if (item.Marker == InfoMarker.Dashed)
            {
                _marker.Color = dashed;
                for (var i = 0; i < Dashes; i++)
                    g.FillRectangle(_marker, x, y + f.Scale(3 + 2 * Dash * i), f.Scale(MarkerWidth), f.Scale(Dash));
            }
        }

        foreach (var row in layout.Table)
        {
            var y = top + f.Scale(row.Y);
            PaneText.Draw(g, row.Row.Key, f.Label, new Point(left + f.Scale(row.KeyX), y), PaneText.Gray);
            PaneText.Draw(g, row.Row.Value, f.Label, new Point(left + f.Scale(row.ValueX), y), PaneText.Black);
        }
    }

    /// <summary>The 96-DPI width of a label or table key, for <see cref="InfoLayout"/>.</summary>
    protected int LabelWidth(string text) => Unscale(PaneText.Width(text, _fonts.Label));

    /// <summary>The 96-DPI width of a big value, for <see cref="InfoLayout"/>.</summary>
    protected int ValueWidth(string text) => Unscale(PaneText.Width(text, _fonts.Value));

    // The height flex (look reference section 7): the info block sits on InfoBottom, the chart area ends 10 px above it, and the
    // graphs stack down from GraphTop to fill what is left.

    /// <summary>The top of <paramref name="info"/>, which ends at <see cref="InfoBottom"/>.</summary>
    protected int InfoTop(InfoLayout info) => InfoBottom - _fonts.Scale(info.Height);

    /// <summary>The chart area's bottom, 10 px above the info block: a graph's bottom captions end here.</summary>
    protected int ChartBottom(InfoLayout info) => InfoTop(info) - _fonts.Scale(10);

    /// <summary>The top of the graph whose top captions are at <paramref name="captionY"/>: 16 px below them.</summary>
    protected int GraphBelowCaptions(int captionY) => captionY + _fonts.Scale(16);

    /// <summary>The next graph's top captions: 19 px below <paramref name="graph"/>'s bottom captions.</summary>
    protected int NextCaptionY(Rectangle graph) => graph.Bottom + _fonts.Scale(2) + _fonts.Scale(19);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateScrollSize();
    }

    /// <summary><see cref="PaintTo"/> fills the background.</summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e) => PaintTo(e.Graphics);

    protected override void OnInvalidated(InvalidateEventArgs e)
    {
        RepaintCount++;
        base.OnInvalidated(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _marker.Dispose();
        base.Dispose(disposing);
    }

    int Unscale(int pixels) => (int)Math.Round(pixels * 96.0 / _fonts.Dpi, MidpointRounding.AwayFromZero);

    void UpdateScrollSize() => AutoScrollMinSize = new Size(0, _fonts.Scale(ContentHeight));
}

/// <summary>The title and subtitle only: stands in for each kind until that kind's own pane is built.</summary>
internal sealed class TitlePane(PaneFonts fonts) : PerformancePane(fonts)
{
    protected override void PaintBody(Graphics g)
    {
    }
}
