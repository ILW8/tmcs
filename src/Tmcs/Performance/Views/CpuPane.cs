using Tmcs.Devices.Cpu;
using Tmcs.Performance.Graphs;
using Tmcs.Settings;

namespace Tmcs.Performance.Views;

/// <summary>
/// The CPU pane: a graph per logical processor in Task Manager's grid, each drawing its kernel time too, and the info block
/// (look reference sections 4, 6 and 7). The grid comes from the device on every paint, since the view hands the pane whatever
/// <see cref="CpuDevice"/> it shows, or none. Painting creates no GDI object: the one painter serves every cell.
/// </summary>
internal sealed class CpuPane : PerformancePane
{
    /// <summary>Between cells, across and down.</summary>
    const int Gap = 4;

    /// <summary>The grid's inset from the content's left and right edges.</summary>
    const int Inset = 2;

    /// <summary>The grid ends this far above the chart area's bottom: the 13-px caption row, which stays empty, and 2 px above it.</summary>
    const int BottomRow = 15;

    /// <summary>A vertical grid line every 10 samples, as on Task Manager's per-processor graphs.</summary>
    const int GridSamples = 10;

    readonly GraphPainter _painter = new(GraphPalettes.Cpu);
    readonly AppSettings _settings;

    // Refilled for every info block, so that building one allocates only its strings and the layout.
    readonly InfoItem[][] _rows = [new InfoItem[2], new InfoItem[3], new InfoItem[1]];
    readonly StaticRow[] _table = new StaticRow[8];
    readonly Func<string, int> _labelWidth, _valueWidth;

    /// <param name="settings">Whether Utilization is Linux-style.</param>
    public CpuPane(PaneFonts fonts, AppSettings settings) : base(fonts)
    {
        _settings = settings;
        _labelWidth = LabelWidth;
        _valueWidth = ValueWidth;
    }

    /// <summary>
    /// Task Manager's CPU content with the chart at its 80-px minimum: caption row 53 + chart 80 + 10 + info 145 + 20 (look
    /// reference section 8). A shorter pane scrolls rather than squash the grid.
    /// </summary>
    public override int ContentHeight => 308;

    /// <summary>The info block for the device shown; empty without a CPU.</summary>
    internal InfoLayout BuildInfo() => Device is CpuDevice cpu ? BuildInfo(cpu) : InfoLayout.Rows([], [], _labelWidth, _valueWidth);

    /// <summary>Logical processor <paramref name="processor"/>'s graph; empty without a CPU.</summary>
    internal Rectangle CellBounds(int processor) =>
        Device is CpuDevice cpu ? Cell(GridBounds(BuildInfo(cpu)), Shape(cpu.Info.LogicalProcessors), processor) : Rectangle.Empty;

    protected override void PaintBody(Graphics g)
    {
        if (Device is not CpuDevice cpu) return;
        var info = BuildInfo(cpu);
        var grid = GridBounds(info);
        // Task Manager draws no "60 seconds" below the grid.
        Captions(g, grid, "% Utilization over 60 seconds", "100%", bottom: false);

        var processors = cpu.Info.LogicalProcessors;
        var shape = Shape(processors);
        var history = cpu.History;
        for (var i = 0; i < processors; i++)
            _painter.Paint(g, Cell(grid, shape, i), history, 1 + 2 * i, 100, GridSamples, Beats, second: 2 + 2 * i);

        PaintInfo(g, info, Color.Empty, Color.Empty); // no item has a legend marker
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _painter.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>rows = max(1, round(√(n / 2))) and columns = ⌈n / rows⌉: 8 × 4 for 32, as Task Manager lays them out.</summary>
    static (int Columns, int Rows) Shape(int processors)
    {
        var rows = Math.Max(1, (int)Math.Round(Math.Sqrt(processors / 2.0)));
        return (Math.Max(1, (processors + rows - 1) / rows), rows);
    }

    /// <summary>One start and length along an axis of <paramref name="count"/> cells: the last cell takes the remainder.</summary>
    static (int Start, int Length) Span(int start, int length, int count, int index, int gap)
    {
        var size = Math.Max(0, length - gap * (count - 1)) / count;
        var offset = index * (size + gap);
        return (start + offset, index == count - 1 ? length - offset : size);
    }

    /// <summary>No group separator, as Task Manager shows its counts.</summary>
    static string Integer(int value) => value.ToString();

    InfoLayout BuildInfo(CpuDevice cpu)
    {
        var info = cpu.Info;
        _rows[0][0] = new InfoItem("Utilization", PerfFormat.Percent(cpu.ShownUtilization(_settings.LinuxStyleCpu)));
        _rows[0][1] = new InfoItem("Speed", PerfFormat.Ghz(cpu.SpeedMhz));
        _rows[1][0] = new InfoItem("Processes", Integer(cpu.Processes));
        _rows[1][1] = new InfoItem("Threads", Integer(cpu.Threads));
        _rows[1][2] = new InfoItem("Handles", Integer(cpu.Handles));
        _rows[2][0] = new InfoItem("Up time", PerfFormat.UpTime(cpu.UpTime));
        _table[0] = new StaticRow("Base speed:", PerfFormat.Ghz(info.BaseMhz));
        _table[1] = new StaticRow("Sockets:", Integer(info.Sockets));
        _table[2] = new StaticRow("Cores:", Integer(info.Cores));
        _table[3] = new StaticRow("Logical processors:", Integer(info.LogicalProcessors));
        _table[4] = new StaticRow("Virtualization:", info.Virtualization);
        _table[5] = new StaticRow("L1 cache:", PerfFormat.Size(info.L1Bytes));
        _table[6] = new StaticRow("L2 cache:", PerfFormat.Size(info.L2Bytes));
        _table[7] = new StaticRow("L3 cache:", PerfFormat.Size(info.L3Bytes));
        return InfoLayout.Rows(_rows, _table, _labelWidth, _valueWidth);
    }

    /// <summary>The grid: inset 2 px from the content's edges, from the first graph's top to 15 px above the chart area's bottom.</summary>
    Rectangle GridBounds(InfoLayout info)
    {
        var inset = Fonts.Scale(Inset);
        var left = ContentLeft + inset;
        var top = GraphTop;
        var bottom = ChartBottom(info) - Fonts.Scale(BottomRow);
        return new Rectangle(left, top, Math.Max(0, ContentRight - inset - left), Math.Max(0, bottom - top));
    }

    /// <summary>Cells are in row-major order, processor 0 at the top left, 4 px apart.</summary>
    Rectangle Cell(Rectangle grid, (int Columns, int Rows) shape, int processor)
    {
        var gap = Fonts.Scale(Gap);
        var (x, width) = Span(grid.X, grid.Width, shape.Columns, processor % shape.Columns, gap);
        var (y, height) = Span(grid.Y, grid.Height, shape.Rows, processor / shape.Columns, gap);
        return new Rectangle(x, y, width, height);
    }
}
