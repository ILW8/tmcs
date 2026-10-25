using Tmcs.Devices.Memory;
using Tmcs.Performance.Graphs;

namespace Tmcs.Performance.Views;

/// <summary>
/// The Memory pane: the usage graph, the composition bar below it, and the info block (look reference sections 3, 4 and 7).
/// The composition block is a fixed 84 px tall and the graph takes the height that is left, down to 80 px, below which the pane
/// scrolls. Everything comes from the device on every paint, and painting creates no GDI object.
/// </summary>
internal sealed class MemoryPane : PerformancePane
{
    /// <summary>The composition block: its caption row, the bar 18 px below the block's top, and the block's whole height.</summary>
    const int BarOffset = 18, BarHeight = 52, BlockHeight = 84;

    static readonly Color BarBorder = Color.FromArgb(0x8B, 0x12, 0xAE);
    static readonly Color InUseFill = Color.FromArgb(0xF4, 0xF2, 0xF4);
    static readonly Color ModifiedFill = Color.FromArgb(0xEE, 0xE6, 0xF1);
    static readonly Color Separator = Color.FromArgb(0xCE, 0xB0, 0xD7);

    readonly GraphPainter _painter = new(GraphPalettes.Memory);

    /// <summary>The bar's border, separators and fills in turn: the color is set before each fill.</summary>
    readonly SolidBrush _brush = new(Color.White);

    // Refilled for every info block, so that building one allocates only its strings and the layout.
    readonly InfoItem[][] _rows = [new InfoItem[2], new InfoItem[2], new InfoItem[2]];
    readonly List<StaticRow> _table = new(4);
    readonly Func<string, int> _labelWidth, _valueWidth;

    public MemoryPane(PaneFonts fonts) : base(fonts)
    {
        _labelWidth = LabelWidth;
        _valueWidth = ValueWidth;
    }

    /// <summary>
    /// Task Manager's Memory content with the graph at its 80-px minimum: caption row 53 + 16 + graph 80 + 21 + composition block
    /// 84 + 10 + info 145 + 20 (look reference section 8).
    /// </summary>
    public override int ContentHeight => 429;

    /// <summary>The usage graph's rectangle; empty without a memory device.</summary>
    internal Rectangle GraphBounds => Device is MemoryDevice memory ? Graph(BuildInfo(memory)) : Rectangle.Empty;

    /// <summary>The composition bar, border included; empty without a memory device.</summary>
    internal Rectangle BarBounds => Device is MemoryDevice memory ? Bar(BuildInfo(memory)) : Rectangle.Empty;

    /// <summary>The In use, Modified, Standby and Free segments' widths, separators included; zero without a memory device.</summary>
    internal (int InUse, int Modified, int Standby, int Free) SegmentWidths() =>
        Device is MemoryDevice memory ? Segments(memory, Bar(BuildInfo(memory)).Width - 2) : default;

    /// <summary>The info block for the device shown; empty without a memory device.</summary>
    internal InfoLayout BuildInfo() =>
        Device is MemoryDevice memory ? BuildInfo(memory) : InfoLayout.Rows([], [], _labelWidth, _valueWidth);

    protected override void PaintBody(Graphics g)
    {
        if (Device is not MemoryDevice memory) return;
        var info = BuildInfo(memory);
        var graph = Graph(info);
        var total = memory.Info.TotalPhysical;
        Captions(g, graph, "Memory usage", PerfFormat.Size(total), bottom: true);
        _painter.Paint(g, graph, memory.History, 0, total, 3);

        PaintComposition(g, memory, info);
        PaintInfo(g, info, Color.Empty, Color.Empty); // no item has a legend marker
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _painter.Dispose();
            _brush.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Each segment is round(inner × part / total), half away from zero, a negative part counting as 0, and no segment is wider
    /// than what the ones before it left; Free takes the rest. A total of 0 leaves everything to Free. The rule is the owner's:
    /// the captures could not determine Task Manager's.
    /// </summary>
    static (int InUse, int Modified, int Standby, int Free) Segments(MemoryDevice memory, int inner)
    {
        inner = Math.Max(0, inner);
        var total = memory.Info.TotalPhysical;
        if (total <= 0) return (0, 0, 0, inner);

        int Part(long bytes, int left) =>
            Math.Min(left, (int)Math.Round((double)inner * Math.Max(0, bytes) / total, MidpointRounding.AwayFromZero));
        var inUse = Part(memory.InUse, inner);
        var modified = Part(memory.Modified, inner - inUse);
        var standby = Part(memory.Standby, inner - inUse - modified);
        return (inUse, modified, standby, inner - inUse - modified - standby);
    }

    /// <summary>The composition caption's y; the bar is 18 px below it.</summary>
    internal int CompositionTop => Device is MemoryDevice memory ? BlockTop(BuildInfo(memory)) : 0;

    /// <summary>The block's top: 10 px above the info block and 84 px tall.</summary>
    int BlockTop(InfoLayout info) => ChartBottom(info) - Fonts.Scale(BlockHeight);

    /// <summary>Content-wide, from the first graph's top to the bottom captions' 2 + 19 px above the block.</summary>
    Rectangle Graph(InfoLayout info)
    {
        var top = GraphBelowCaptions(CaptionY);
        var bottom = BlockTop(info) - Fonts.Scale(2) - Fonts.Scale(19);
        return new Rectangle(ContentLeft, top, Math.Max(0, ContentRight - ContentLeft), Math.Max(0, bottom - top));
    }

    Rectangle Bar(InfoLayout info) =>
        new(ContentLeft, BlockTop(info) + Fonts.Scale(BarOffset), Math.Max(0, ContentRight - ContentLeft), Fonts.Scale(BarHeight));

    void PaintComposition(Graphics g, MemoryDevice memory, InfoLayout info)
    {
        PaneText.Draw(g, "Memory composition", Fonts.Caption, new Point(ContentLeft, BlockTop(info)), PaneText.Gray);

        var bar = Bar(info);
        if (bar.Width < 3 || bar.Height < 3) return;
        Fill(g, BarBorder, bar);

        var inner = new Rectangle(bar.X + 1, bar.Y + 1, bar.Width - 2, bar.Height - 2);
        var (inUse, modified, standby, free) = Segments(memory, inner.Width);
        var x = inner.X;
        var first = true; // the first segment drawn has no separator, whichever it is
        Segment(g, ref x, inner, inUse, InUseFill, ref first);
        Segment(g, ref x, inner, modified, ModifiedFill, ref first);
        Segment(g, ref x, inner, standby, Color.White, ref first);
        Segment(g, ref x, inner, free, Color.White, ref first);
    }

    /// <summary>One segment of <paramref name="width"/> px at <paramref name="x"/>; all but the first drawn start with a 1-px line.</summary>
    void Segment(Graphics g, ref int x, Rectangle inner, int width, Color fill, ref bool first)
    {
        if (width <= 0) return;
        var start = x;
        x += width;
        if (first) first = false;
        else
        {
            Fill(g, Separator, new Rectangle(start, inner.Y, 1, inner.Height));
            start++;
            width--;
        }

        if (width > 0) Fill(g, fill, new Rectangle(start, inner.Y, width, inner.Height));
    }

    void Fill(Graphics g, Color color, Rectangle r)
    {
        _brush.Color = color;
        g.FillRectangle(_brush, r);
    }

    InfoLayout BuildInfo(MemoryDevice memory)
    {
        var info = memory.Info;
        _rows[0][0] = new InfoItem("In use (Compressed)", PerfFormat.Size(memory.InUse) + " (" + PerfFormat.Size(memory.Compressed) + ")");
        _rows[0][1] = new InfoItem("Available", PerfFormat.Size(memory.Available));
        _rows[1][0] = new InfoItem("Committed", PerfFormat.Pair(memory.Committed, memory.CommitLimit, "GB", 1 << 30));
        _rows[1][1] = new InfoItem("Cached", PerfFormat.Size(memory.Cached));
        _rows[2][0] = new InfoItem("Paged pool", PerfFormat.Size(memory.PagedPool));
        _rows[2][1] = new InfoItem("Non-paged pool", PerfFormat.Size(memory.NonPagedPool));

        // A row the firmware could not tell us about is left out, not shown as 0.
        _table.Clear();
        if (info.SpeedMhz > 0) _table.Add(new StaticRow("Speed:", info.SpeedMhz + " MHz"));
        if (info.Slots > 0) _table.Add(new StaticRow("Slots used:", info.SlotsUsed + " of " + info.Slots));
        if (!string.IsNullOrWhiteSpace(info.FormFactor)) _table.Add(new StaticRow("Form factor:", info.FormFactor));
        if (info.HardwareReserved > 0) _table.Add(new StaticRow("Hardware reserved:", PerfFormat.Size(info.HardwareReserved)));
        return InfoLayout.Rows(_rows, _table, _labelWidth, _valueWidth);
    }
}
