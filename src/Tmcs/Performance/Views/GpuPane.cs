using Tmcs.Devices.Gpu;
using Tmcs.Performance.Graphs;

namespace Tmcs.Performance.Views;

/// <summary>
/// The GPU pane: the 3D, Copy, Video Encode and Video Decode graphs in a 2 × 2 grid, the dedicated and shared memory graphs, and
/// the info block in columns (look reference sections 4, 7 and 8). Task Manager's content overflows its pane, so the layout is
/// fixed and scrolls; only widths flex. The engine dropdowns are out of scope, and so is their chevron: each name keeps its place.
/// Everything comes from the device on every paint, and painting creates no GDI object: the one painter serves every graph.
/// </summary>
internal sealed class GpuPane : PerformancePane
{
    /// <summary>The engine area's inset from the content's left and right edges, and the gap between its two columns.</summary>
    const int EngineInset = 4, EngineGap = 8;

    /// <summary>An engine's name starts this far right of its graph's left edge.</summary>
    const int NameIndent = 13;

    /// <summary>A vertical grid line every 4 samples on the engine graphs, and every 3 on the memory graphs.</summary>
    const int EngineGridSamples = 4, MemoryGridSamples = 3;

    /// <summary>The memory graphs' inset from the content's left and right edges, their tops, and their height.</summary>
    const int MemoryInset = 2, DedicatedY = 279, SharedY = 366, MemoryHeight = 67;

    /// <summary>The info block's top, whatever its height: it stays put as the rest of the layout does.</summary>
    const int InfoY = 450;

    static readonly string[] EngineNames = ["3D", "Copy", "Video Encode", "Video Decode"];

    /// <summary>Each engine row's name, graph top and graph height.</summary>
    static readonly (int Label, int Graph, int Height)[] EngineRows = [(55, 77, 73), (156, 178, 74)];

    readonly GraphPainter _painter = new(GraphPalettes.Gpu);

    // Refilled for every info block, so that building one allocates only its strings and the layout. The second column has the
    // temperature or not.
    readonly InfoItem[] _first = new InfoItem[2];
    readonly List<InfoItem> _second = new(3);
    readonly IReadOnlyList<InfoItem>[] _columns;
    readonly StaticRow[] _table = new StaticRow[5];
    readonly Func<string, int> _labelWidth, _valueWidth;

    public GpuPane(PaneFonts fonts) : base(fonts)
    {
        _columns = [_first, _second];
        _labelWidth = LabelWidth;
        _valueWidth = ValueWidth;
    }

    /// <summary>
    /// Task Manager's GPU content: the info block 450 to 595 and 20 px below it (look reference sections 7 and 8). It stays 615
    /// without a temperature, whose row the block then leaves out.
    /// </summary>
    public override int ContentHeight => 615;

    /// <summary>Engine <paramref name="engine"/>'s graph, 0 to 3 for 3D, Copy, Video Encode and Video Decode; empty without a GPU.</summary>
    internal Rectangle EngineBounds(int engine) => Device is GpuDevice ? Engine(engine) : Rectangle.Empty;

    /// <summary>The dedicated memory graph's rectangle; empty without a GPU.</summary>
    internal Rectangle DedicatedBounds => Device is GpuDevice ? MemoryGraph(DedicatedY) : Rectangle.Empty;

    /// <summary>The shared memory graph's rectangle; empty without a GPU.</summary>
    internal Rectangle SharedBounds => Device is GpuDevice ? MemoryGraph(SharedY) : Rectangle.Empty;

    /// <summary>The info block for the device shown; empty without a GPU.</summary>
    internal InfoLayout BuildInfo() =>
        Device is GpuDevice gpu ? BuildInfo(gpu) : InfoLayout.Columns([], [], _labelWidth, _valueWidth);

    /// <summary>The info block's top.</summary>
    internal int InfoTopY => Fonts.Scale(InfoY);

    protected override int InfoTop(InfoLayout info) => InfoTopY;

    protected override void PaintBody(Graphics g)
    {
        if (Device is not GpuDevice gpu) return;
        var f = Fonts;
        var history = gpu.History;
        var counters = gpu.Counters;

        for (var i = 0; i < EngineNames.Length; i++)
        {
            var graph = Engine(i);
            var labelY = f.Scale(EngineRows[i / 2].Label);
            PaneText.Draw(g, EngineNames[i], f.Engine, new Point(graph.X + f.Scale(NameIndent), labelY), PaneText.Black);
            var percent = PerfFormat.Percent(EngineValue(counters, i));
            var right = graph.Right + f.Scale(2);
            PaneText.Draw(g, percent, f.Caption, new Point(right - PaneText.Width(percent, f.Caption), labelY + f.Scale(2)), PaneText.Gray);
            _painter.Paint(g, graph, history, 1 + i, 100, EngineGridSamples);
        }

        var info = gpu.Info;
        var dedicated = MemoryGraph(DedicatedY);
        Captions(g, dedicated, "Dedicated GPU memory usage", PerfFormat.Size(info.DedicatedTotal), bottom: false);
        _painter.Paint(g, dedicated, history, 5, info.DedicatedTotal, MemoryGridSamples);
        var shared = MemoryGraph(SharedY);
        Captions(g, shared, "Shared GPU memory usage", PerfFormat.Size(info.SharedTotal), bottom: false);
        _painter.Paint(g, shared, history, 6, info.SharedTotal, MemoryGridSamples);

        PaintInfo(g, BuildInfo(gpu), Color.Empty, Color.Empty); // no item has a legend marker
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _painter.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>The latest value of engine <paramref name="engine"/>'s series.</summary>
    static float EngineValue(in GpuCounters counters, int engine) => engine switch
    {
        0 => counters.ThreeD,
        1 => counters.Copy,
        2 => counters.VideoEncode,
        _ => counters.VideoDecode,
    };

    /// <summary>
    /// The engine area spans the content inset 4 px each side; its two columns are ⌊(width − 8) / 2⌋ wide, 8 px apart, in
    /// row-major order.
    /// </summary>
    Rectangle Engine(int engine)
    {
        var f = Fonts;
        var inset = f.Scale(EngineInset);
        var gap = f.Scale(EngineGap);
        var left = ContentLeft + inset;
        var width = Math.Max(0, (ContentRight - inset - left - gap) / 2);
        var row = EngineRows[engine / 2];
        return new Rectangle(engine % 2 == 0 ? left : left + width + gap, f.Scale(row.Graph), width, f.Scale(row.Height));
    }

    /// <summary>A memory graph at <paramref name="top"/>: the content inset 2 px each side, 67 px tall.</summary>
    Rectangle MemoryGraph(int top)
    {
        var inset = Fonts.Scale(MemoryInset);
        var left = ContentLeft + inset;
        return new Rectangle(left, Fonts.Scale(top), Math.Max(0, ContentRight - inset - left), Fonts.Scale(MemoryHeight));
    }

    InfoLayout BuildInfo(GpuDevice gpu)
    {
        var info = gpu.Info;
        var c = gpu.Counters;
        _first[0] = new InfoItem("Utilization", PerfFormat.Percent(c.Utilization));
        _first[1] = new InfoItem("GPU Memory",
            PerfFormat.Pair(c.DedicatedUsed + c.SharedUsed, info.DedicatedTotal + info.SharedTotal, "GB", 1 << 30));
        _second.Clear();
        _second.Add(new InfoItem("Dedicated GPU memory", PerfFormat.Pair(c.DedicatedUsed, info.DedicatedTotal, "GB", 1 << 30)));
        _second.Add(new InfoItem("Shared GPU memory", PerfFormat.Pair(c.SharedUsed, info.SharedTotal, "GB", 1 << 30)));
        if (c.TemperatureC is { } t) _second.Add(new InfoItem("GPU Temperature", PerfFormat.Celsius(t)));

        _table[0] = new StaticRow("Driver version:", info.DriverVersion);
        _table[1] = new StaticRow("Driver date:", info.DriverDate);
        _table[2] = new StaticRow("DirectX version:", info.DirectX);
        _table[3] = new StaticRow("Physical location:", info.Location);
        _table[4] = new StaticRow("Hardware reserved memory:", PerfFormat.Size(info.HardwareReserved));
        return InfoLayout.Columns(_columns, _table, _labelWidth, _valueWidth);
    }
}
