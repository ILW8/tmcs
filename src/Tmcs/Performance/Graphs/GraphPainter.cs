using System.Drawing.Drawing2D;

namespace Tmcs.Performance.Graphs;

/// <summary>
/// Paints one Task Manager graph. Every pen, brush, path and the point buffer are created once here, so
/// <see cref="Paint"/> allocates nothing and can run on every tick for every visible graph.
/// </summary>
internal sealed class GraphPainter : IDisposable
{
    /// <summary>Narrower or shorter than this, a graph has no interior and is not painted.</summary>
    const int MinSide = 3;

    /// <summary>The side of the bounds GDI+ reports for an infinite clip (2^23), whatever translation applies.</summary>
    const float InfiniteClipSide = 8388608f;

    readonly SolidBrush _border, _topCorner, _bottomCorner, _grid, _fill, _gridInFill, _secondFill, _gridInSecondFill;
    readonly Pen _line, _secondLine;
    readonly GraphicsPath _clip = new();
    readonly PointF[] _points = new PointF[History.Capacity + 2];
    readonly bool _secondFilled;

    public GraphPainter(GraphPalette palette)
    {
        Palette = palette;
        _border = new SolidBrush(palette.Border);
        _topCorner = new SolidBrush(palette.TopCorner);
        _bottomCorner = new SolidBrush(palette.BottomCorner);
        _grid = new SolidBrush(palette.Grid);
        _fill = new SolidBrush(palette.Fill);
        _gridInFill = new SolidBrush(palette.GridInFill);
        _secondFilled = !palette.SecondFill.IsEmpty;
        _secondFill = new SolidBrush(palette.SecondFill);
        _gridInSecondFill = new SolidBrush(palette.GridInSecondFill);
        _line = new Pen(palette.Line, 1);
        _secondLine = new Pen(palette.SecondLine, 1);
        if (!_secondFilled) _secondLine.DashStyle = DashStyle.Dash;
    }

    public GraphPalette Palette { get; }

    /// <summary>
    /// Paints a graph whose border is the outer pixels of <paramref name="bounds"/>, or nothing when <paramref name="bounds"/>
    /// is narrower or shorter than 3 px. Values are clamped to [0, <paramref name="max"/>], and NaN counts as 0;
    /// <paramref name="max"/> &lt;= 0 draws no data. <paramref name="gridSamples"/> 0 means no grid. <paramref name="second"/>
    /// is the series drawn in the palette's second style, or -1.
    /// <para>
    /// <see cref="Graphics.Save"/> allocates on every call, so the clip and the modes this changes are put back by hand. The
    /// caller's clip comes back as its bounding rectangle, or as infinite when it was.
    /// </para>
    /// </summary>
    public void Paint(Graphics g, Rectangle bounds, History history, int series, float max, int gridSamples, int second = -1)
    {
        if (bounds.Width < MinSide || bounds.Height < MinSide) return;
        int left = bounds.X, right = bounds.Right - 1, top = bounds.Y, bottom = bounds.Bottom - 1;
        var clip = g.ClipBounds;
        var smoothing = g.SmoothingMode;
        var pixelOffset = g.PixelOffsetMode;
        try
        {
            // The pixel offset mode moves a clip set in world coordinates, so it is set first, and put back before the clip.
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.IntersectClip(new Rectangle(left + 1, top + 1, bounds.Width - 2, bounds.Height - 2));
            g.FillRectangle(Brushes.White, left, top, bounds.Width, bounds.Height);
            DrawGrid(g, _grid, history, left, right, top, bottom, gridSamples);

            if (max > 0 && history.Count > 1)
            {
                var first = history.FirstVisible;
                var n = history.Count - first;
                if (n > 1)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    FillSeries(g, _fill, _gridInFill, history, series, max, gridSamples, first, left, right, top, bottom);
                    if (second >= 0 && _secondFilled)
                        FillSeries(g, _secondFill, _gridInSecondFill, history, second, max, gridSamples, first, left, right, top, bottom);
                    if (second >= 0)
                    {
                        BuildLine(history, second, max, first, left, right, top, bottom);
                        g.DrawLines(_secondLine, new ReadOnlySpan<PointF>(_points, 0, n));
                    }
                    BuildLine(history, series, max, first, left, right, top, bottom);
                    g.DrawLines(_line, new ReadOnlySpan<PointF>(_points, 0, n));
                }
            }
        }
        finally
        {
            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = pixelOffset;
            // An infinite clip reports GDI+'s 2^23 rectangle as its bounds; it is put back as infinite, not as that rectangle.
            if (clip.Width >= InfiniteClipSide && clip.Height >= InfiniteClipSide) g.ResetClip();
            else g.SetClip(clip);
        }

        DrawBorder(g, left, right, top, bottom);
    }

    void FillSeries(Graphics g, Brush fill, Brush gridInFill, History history, int series, float max, int gridSamples,
        int first, int left, int right, int top, int bottom)
    {
        var n = BuildLine(history, series, max, first, left, right, top, bottom);
        _points[n] = new PointF(_points[n - 1].X, bottom);
        _points[n + 1] = new PointF(_points[0].X, bottom);
        var polygon = new ReadOnlySpan<PointF>(_points, 0, n + 2);
        g.FillPolygon(fill, polygon);

        if (gridSamples <= 0) return;
        // The grid shows through a fill in a darker color; the clip is the polygon itself. The clip before it comes back by
        // its bounds, as in Paint.
        var clip = g.ClipBounds;
        _clip.Reset();
        _clip.AddPolygon(polygon);
        g.SetClip(_clip, CombineMode.Intersect);
        g.SmoothingMode = SmoothingMode.None;
        DrawGrid(g, gridInFill, history, left, right, top, bottom, gridSamples);
        g.SetClip(clip);
        g.SmoothingMode = SmoothingMode.AntiAlias;
    }

    /// <summary>
    /// Puts the points of <paramref name="series"/> in the buffer, each rounded to a whole pixel as Task Manager does: at a
    /// fractional y, an anti-aliased 1-px line is two rows of a lighter color. The x rounds as the grid's does, so points on
    /// a grid sample sit on its line.
    /// </summary>
    int BuildLine(History history, int series, float max, int first, int left, int right, int top, int bottom)
    {
        var latest = history.LatestTime;
        var width = (double)(right - left);
        var height = (double)(bottom - top);
        var n = 0;
        for (var i = first; i < history.Count; i++)
        {
            var value = history.ValueAt(series, i);
            var v = float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, max); // Clamp passes NaN through, and GDI+ must not get it
            var x = right - (latest - history.TimeAt(i)) / (double)History.Window100Ns * width;
            var y = bottom - v / max * height;
            _points[n++] = new PointF((float)Math.Round(x, MidpointRounding.AwayFromZero), (float)Math.Round(y, MidpointRounding.AwayFromZero));
        }
        return n;
    }

    static void DrawGrid(Graphics g, Brush brush, History history, int left, int right, int top, int bottom, int gridSamples)
    {
        if (gridSamples <= 0) return;
        var width = right - left + 1;
        var height = bottom - top;
        for (var k = 1; k <= 9; k++)
            g.FillRectangle(brush, left, (int)Math.Ceiling(top + k * height / 10.0), width, 1);

        if (history.Count == 0) return;
        var latest = history.LatestTime;
        for (var i = history.FirstVisible; i < history.Count; i++)
        {
            if (history.SequenceAt(i) % gridSamples != 0) continue;
            var x = right - (latest - history.TimeAt(i)) / (double)History.Window100Ns * (right - left);
            g.FillRectangle(brush, (int)Math.Round(x, MidpointRounding.AwayFromZero), top, 1, height + 1);
        }
    }

    void DrawBorder(Graphics g, int left, int right, int top, int bottom)
    {
        var w = right - left + 1;
        var h = bottom - top + 1;
        g.FillRectangle(_border, left, top, w, 1);
        g.FillRectangle(_border, left, bottom, w, 1);
        g.FillRectangle(_border, left, top, 1, h);
        g.FillRectangle(_border, right, top, 1, h);
        g.FillRectangle(_topCorner, left, top, 1, 1);
        g.FillRectangle(_topCorner, right, top, 1, 1);
        g.FillRectangle(_bottomCorner, left, bottom, 1, 1);
        g.FillRectangle(_bottomCorner, right, bottom, 1, 1);
    }

    public void Dispose()
    {
        _border.Dispose(); _topCorner.Dispose(); _bottomCorner.Dispose(); _grid.Dispose(); _fill.Dispose();
        _gridInFill.Dispose(); _secondFill.Dispose(); _gridInSecondFill.Dispose();
        _line.Dispose(); _secondLine.Dispose(); _clip.Dispose();
    }
}
