using Tmcs.Devices.Disk;
using Tmcs.Performance.Graphs;

namespace Tmcs.Performance.Views;

/// <summary>
/// The Disk pane: the Active time graph, the Disk transfer rate graph below it, and the info block (look reference sections 4, 6
/// and 7). The transfer-rate graph is a fixed 67 px tall and Active time takes the height that is left. No code floors it at
/// 80 px: that follows from <see cref="ContentHeight"/>, below which the pane scrolls. Everything comes from the device on every
/// paint, and painting creates no GDI object.
/// </summary>
internal sealed class DiskPane : PerformancePane
{
    const int TransferHeight = 67;

    /// <summary>The bottom captions' box: 2 px below the graph and 13 px tall.</summary>
    const int BottomCaptions = 15;

    readonly GraphPainter _painter = new(GraphPalettes.Disk);

    // Refilled for every info block, so that building one allocates only its strings and the layout.
    readonly InfoItem[][] _rows = [new InfoItem[2], new InfoItem[2]];
    readonly List<StaticRow> _table = new(5);
    readonly Func<string, int> _labelWidth, _valueWidth;

    public DiskPane(PaneFonts fonts) : base(fonts)
    {
        _labelWidth = LabelWidth;
        _valueWidth = ValueWidth;
    }

    /// <summary>
    /// Task Manager's Disk content with Active time at its 80-px minimum: caption row 53 + 16 + graph 80 + 21 + 16 + transfer
    /// graph 67 + 15 + 10 + info 94 + 20 (look reference section 8).
    /// </summary>
    public override int ContentHeight => 392;

    /// <summary>The Active time graph's rectangle; empty without a disk device.</summary>
    internal Rectangle ActiveBounds => Device is DiskDevice disk ? Active(BuildInfo(disk)) : Rectangle.Empty;

    /// <summary>The Disk transfer rate graph's rectangle; empty without a disk device.</summary>
    internal Rectangle TransferBounds => Device is DiskDevice disk ? Transfer(BuildInfo(disk)) : Rectangle.Empty;

    /// <summary>The info block for the device shown; empty without a disk device.</summary>
    internal InfoLayout BuildInfo() =>
        Device is DiskDevice disk ? BuildInfo(disk) : InfoLayout.Rows([], [], _labelWidth, _valueWidth);

    /// <summary>
    /// The transfer graph's scale, from the larger of the visible read and write rates; default without a disk device.
    /// </summary>
    internal (float Max, string Caption) TransferScale => Device is DiskDevice disk ? ScaleOf(disk) : default;

    static (float Max, string Caption) ScaleOf(DiskDevice disk) =>
        GraphScales.Disk(Math.Max(disk.History.MaxVisible(1), disk.History.MaxVisible(2)));

    protected override void PaintBody(Graphics g)
    {
        if (Device is not DiskDevice disk) return;
        var info = BuildInfo(disk);
        var history = disk.History;

        var active = Active(info);
        Captions(g, active, "Active time", "100%", bottom: true);
        _painter.Paint(g, active, history, 0, 100, 3, Beats);

        var transfer = Transfer(info);
        var scale = ScaleOf(disk);
        Captions(g, transfer, "Disk transfer rate", scale.Caption, bottom: true);
        _painter.Paint(g, transfer, history, 1, scale.Max, 3, Beats, second: 2);

        PaintInfo(g, info, GraphPalettes.Disk.Line, GraphPalettes.Disk.SecondLine);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _painter.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Fixed height, ending where the bottom captions leave the chart area.</summary>
    Rectangle Transfer(InfoLayout info)
    {
        var height = Fonts.Scale(TransferHeight);
        var bottom = ChartBottom(info) - Fonts.Scale(BottomCaptions);
        return new Rectangle(ContentLeft, bottom - height, Math.Max(0, ContentRight - ContentLeft), height);
    }

    /// <summary>
    /// From the first graph's top down to where the transfer graph's top captions sit 19 px below its bottom captions.
    /// </summary>
    Rectangle Active(InfoLayout info)
    {
        var top = GraphBelowCaptions(CaptionY);
        var bottom = Transfer(info).Y - Fonts.Scale(16) - Fonts.Scale(2) - Fonts.Scale(19);
        return new Rectangle(ContentLeft, top, Math.Max(0, ContentRight - ContentLeft), Math.Max(0, bottom - top));
    }

    InfoLayout BuildInfo(DiskDevice disk)
    {
        var info = disk.Info;
        _rows[0][0] = new InfoItem("Active time", PerfFormat.Percent(disk.ActivePercent));
        _rows[0][1] = new InfoItem("Average response time", PerfFormat.Milliseconds(disk.ResponseMs));
        _rows[1][0] = new InfoItem("Read speed", PerfFormat.DiskRate(disk.ReadBytesPerSecond), InfoMarker.Solid);
        _rows[1][1] = new InfoItem("Write speed", PerfFormat.DiskRate(disk.WriteBytesPerSecond), InfoMarker.Dashed);

        _table.Clear();
        _table.Add(new StaticRow("Capacity:", PerfFormat.Size(info.Capacity)));
        if (info.Formatted > 0) _table.Add(new StaticRow("Formatted:", PerfFormat.Size(info.Formatted)));
        _table.Add(new StaticRow("System disk:", info.IsSystem ? "Yes" : "No"));
        _table.Add(new StaticRow("Page file:", info.HasPageFile ? "Yes" : "No"));
        _table.Add(new StaticRow("Type:", info.Type));
        return InfoLayout.Rows(_rows, _table, _labelWidth, _valueWidth);
    }
}
