using Tmcs.Devices.Network;
using Tmcs.Performance.Graphs;

namespace Tmcs.Performance.Views;

/// <summary>
/// The Network pane: the Throughput graph, and the info block of Send and Receive beside the adapter's table (look reference
/// sections 4, 6 and 7). The graph takes the height the info block leaves, down to 80 px, below which the pane scrolls. The
/// block, and so <see cref="ContentHeight"/>, follows the adapter: Wi-Fi adds its SSID and signal rows. Everything comes from
/// the device on every paint but the Send and Receive column's width, and painting creates no GDI object.
/// </summary>
internal sealed class NetworkPane : PerformancePane
{
    /// <summary>The bottom captions' box: 2 px below the graph and 13 px tall.</summary>
    const int BottomCaptions = 15;

    /// <summary>
    /// The content but the info block, with the graph at its 80-px minimum: caption row 53 + 16 + graph 80 + 2 + 13 + 10, and 20
    /// below the block (look reference section 8).
    /// </summary>
    const int AroundInfo = 194;

    const string SignalKey = "Signal strength:";
    const int SignalRowHeight = 19;

    /// <summary>
    /// The signal glyph's bars' tops. Bar n's outline is 4 px wide at x 3n, so it shares a side with the next, and runs down to y 14
    /// (look reference section 6: the fill reaches y 13, the bottom outline is y 14, and the baseline y 15).
    /// </summary>
    static readonly int[] BarTops = [10, 7, 5, 2, 0];

    readonly GraphPainter _painter = new(GraphPalettes.Network);
    readonly SolidBrush _signalOutline = new(Color.FromArgb(0xBB, 0x81, 0x4E));
    readonly SolidBrush _signalFill = new(Color.FromArgb(0xDB, 0xBA, 0xA0));

    // Refilled for every info block, so that building one allocates only its strings and the layout.
    readonly InfoItem[][] _rows = [new InfoItem[1], new InfoItem[1]];
    readonly List<StaticRow> _table = new(7);
    readonly Func<string, int> _labelWidth, _valueWidth;

    /// <summary>
    /// The table <see cref="ContentHeight"/> counts, refilled whenever it is asked for, which is during every paint: its own list,
    /// so it never refills <see cref="_table"/> behind a layout built from it.
    /// </summary>
    readonly List<StaticRow> _heightTable = new(7);

    /// <summary>
    /// The widest Send or Receive value shown, in 96-DPI units, and the least width of their column: Task Manager's column never
    /// narrows, and every adapter shares it (look reference section 7).
    /// </summary>
    int _widestRate;

    public NetworkPane(PaneFonts fonts) : base(fonts)
    {
        _labelWidth = LabelWidth;
        _valueWidth = ValueWidth;
    }

    /// <summary>
    /// The info block's height + 194 (look reference section 8): 288 for Ethernet's 94, 303 for Wi-Fi's 109, and 288, the least
    /// an adapter needs, without one. It measures no text, as the pane asks for it while it paints.
    /// </summary>
    public override int ContentHeight =>
        AroundInfo + InfoLayout.HeightOf(_rows.Length, Table(_heightTable, Device as NetworkDevice));

    /// <summary>The Throughput graph's rectangle; empty without an adapter.</summary>
    internal Rectangle GraphBounds => Device is NetworkDevice net ? Graph(BuildInfo(net)) : Rectangle.Empty;

    /// <summary>The info block for the adapter shown; empty without one.</summary>
    internal InfoLayout BuildInfo() =>
        Device is NetworkDevice net ? BuildInfo(net) : InfoLayout.Rows([], [], _labelWidth, _valueWidth);

    /// <summary>The graph's scale, from the larger of the visible receive and send rates; default without an adapter.</summary>
    internal (float Max, string Caption) ThroughputScale => Device is NetworkDevice net ? ScaleOf(net) : default;

    /// <summary>The Send and Receive column's least width, in 96-DPI units.</summary>
    internal int WidestRate => _widestRate;

    static (float Max, string Caption) ScaleOf(NetworkDevice net) =>
        GraphScales.Network(Math.Max(net.History.MaxVisible(0), net.History.MaxVisible(1)));

    protected override void PaintBody(Graphics g)
    {
        if (Device is not NetworkDevice net) return;
        var info = BuildInfo(net);

        var graph = Graph(info);
        var scale = ScaleOf(net);
        Captions(g, graph, "Throughput", scale.Caption, bottom: true);
        _painter.Paint(g, graph, net.History, 0, scale.Max, 3, second: 1);

        PaintInfo(g, info, GraphPalettes.Network.Line, GraphPalettes.Network.SecondLine);
        if (net.Wifi is { } wifi) PaintSignal(g, info, NetworkDevice.SignalBars(wifi.SignalQuality));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _painter.Dispose();
            _signalOutline.Dispose();
            _signalFill.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Content-wide, from the first graph's top to its bottom captions' 15 px above the chart area's bottom.</summary>
    Rectangle Graph(InfoLayout info)
    {
        var top = GraphBelowCaptions(CaptionY);
        var bottom = ChartBottom(info) - Fonts.Scale(BottomCaptions);
        return new Rectangle(ContentLeft, top, Math.Max(0, ContentRight - ContentLeft), Math.Max(0, bottom - top));
    }

    InfoLayout BuildInfo(NetworkDevice net)
    {
        var send = PerfFormat.BitRate(net.SendBitsPerSecond);
        var receive = PerfFormat.BitRate(net.ReceiveBitsPerSecond);
        _widestRate = Math.Max(_widestRate, Math.Max(ValueWidth(send), ValueWidth(receive)));
        _rows[0][0] = new InfoItem("Send", send, InfoMarker.Dashed);
        _rows[1][0] = new InfoItem("Receive", receive, InfoMarker.Solid);
        return InfoLayout.Rows(_rows, Table(_table, net), _labelWidth, _valueWidth, _widestRate);
    }

    /// <summary>
    /// Fills <paramref name="table"/> with <paramref name="net"/>'s rows, or none without an adapter, and returns it. Task Manager
    /// leaves out an empty DNS suffix. An address the adapter lacks is left out too, and so is an unknown connection type (a
    /// Wi-Fi adapter without its details, or with a PHY type of no name), and the SSID and the signal strength without details.
    /// </summary>
    static List<StaticRow> Table(List<StaticRow> table, NetworkDevice? net)
    {
        table.Clear();
        if (net is null) return table;
        var info = net.Info;
        var wifi = net.Wifi;
        var connection = net.ConnectionType;
        table.Add(new StaticRow("Adapter name:", info.Alias));
        if (wifi is not null) table.Add(new StaticRow("SSID:", wifi.Ssid));
        if (!string.IsNullOrEmpty(info.DnsSuffix)) table.Add(new StaticRow("DNS name:", info.DnsSuffix));
        if (connection.Length > 0) table.Add(new StaticRow("Connection type:", connection));
        if (info.IPv4 is not null) table.Add(new StaticRow("IPv4 address:", info.IPv4));
        if (info.IPv6 is not null) table.Add(new StaticRow("IPv6 address:", info.IPv6));
        if (wifi is not null) table.Add(new StaticRow(SignalKey, "", SignalRowHeight)); // its value is the glyph, not text
        return table;
    }

    /// <summary>The signal row's glyph, 16 × 16 at the value column's x − 2 and the row's y, with <paramref name="bars"/> bars filled.</summary>
    void PaintSignal(Graphics g, InfoLayout info, int bars)
    {
        var table = info.Table;
        for (var i = 0; i < table.Count; i++)
        {
            var row = table[i];
            if (row.Row.Key != SignalKey) continue;
            var at = new Point(ContentLeft + Fonts.Scale(row.ValueX) - Fonts.Scale(2), InfoTop(info) + Fonts.Scale(row.Y));
            for (var bar = 0; bar < BarTops.Length; bar++)
            {
                var top = BarTops[bar];
                Fill(g, _signalOutline, at, 3 * bar, top, 4, 15 - top);
                Fill(g, bar < bars ? _signalFill : Brushes.White, at, 3 * bar + 1, top + 1, 2, 13 - top);
            }

            Fill(g, _signalOutline, at, 0, 15, 16, 1); // the baseline
        }
    }

    /// <summary>A rectangle of the glyph in 96-DPI units from its top left. Both edges are scaled, so shared outlines stay shared.</summary>
    void Fill(Graphics g, Brush brush, Point at, int x, int y, int width, int height)
    {
        var f = Fonts;
        g.FillRectangle(brush, at.X + f.Scale(x), at.Y + f.Scale(y), f.Scale(x + width) - f.Scale(x), f.Scale(y + height) - f.Scale(y));
    }
}
