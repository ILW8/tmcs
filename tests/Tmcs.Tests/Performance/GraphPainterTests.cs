using System.Drawing.Drawing2D;
using Tmcs.Native;
using Tmcs.Performance;
using Tmcs.Performance.Graphs;

namespace Tmcs.Tests.Performance;

public class GraphPainterTests
{
    const long Second = 10_000_000;

    static History Steady(float total, float kernel)
    { var h = new History(2); for (var i = 0; i < 60; i++) h.Add((i + 1) * Second, [total, kernel]); return h; }

    /// <summary>Every point a timer sample at Normal speed, so every point is a beat.</summary>
    static GridBeats Timed(History h)
    { var b = new GridBeats(); for (var i = 0; i < h.Count; i++) b.Note(h.TimeAt(i), timed: true, Second); return b; }

    static int[,] Paint(History h, int gridSamples = 10, int second = 1, int width = 122, GridBeats? beats = null)
    { using var bmp = new Bitmap(width, 76); using var g = Graphics.FromImage(bmp);
      using var p = new GraphPainter(GraphPalettes.Cpu); p.Paint(g, new Rectangle(0, 0, width, 76), h, 0, 100, gridSamples, beats ?? Timed(h), second);
      var px = new int[width, 76]; for (var x = 0; x < width; x++) for (var y = 0; y < 76; y++) px[x, y] = bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF;
      return px; }

    static uint GuiResources(uint flags) => User32.GetGuiResources(Kernel32.GetCurrentProcess(), flags);

    [Fact] public void Border_has_lighter_corners()
    { var px = Paint(new History(2));
      Assert.Equal(0x117DBB, px[0, 40]); Assert.Equal(0x117DBB, px[121, 40]);
      Assert.Equal(0x3A94C7, px[0, 0]); Assert.Equal(0x3A94C7, px[121, 0]);
      Assert.Equal(0x1F85BF, px[0, 75]); Assert.Equal(0x1F85BF, px[121, 75]); }

    [Fact] public void Empty_graph_is_white_with_horizontal_grid_rows()
    { var px = Paint(new History(2));
      Assert.Equal(0xD9EAF4, px[50, 8]); Assert.Equal(0xD9EAF4, px[50, 68]); Assert.Equal(0xFFFFFF, px[50, 20]); }

    [Fact] public void Mini_graph_has_no_grid()
    { Assert.Equal(0xFFFFFF, Paint(new History(2), gridSamples: 0)[50, 8]); }

    [Fact] public void Half_load_fills_the_lower_half()
    { var px = Paint(Steady(50, 0), second: -1);
      Assert.Equal(0xF1F6FA, px[50, 70]); Assert.Equal(0xFFFFFF, px[50, 20]); Assert.Equal(0xCEE2F0, px[50, 45]); }

    // Task Manager rounds each point to a whole pixel, so a steady value is one row in the line's own color. At its exact y,
    // 50.25, the line would be two rows of a lighter color.
    [Fact] public void Steady_line_is_one_row_of_the_line_color()
    { var px = Paint(Steady(33, 0), second: -1); // y = 75 − 33% of 75 = 50.25
      Assert.Equal(0xFFFFFF, px[50, 49]); Assert.Equal(0x117DBB, px[50, 50]); Assert.Equal(0xF1F6FA, px[50, 51]); }

    [Fact] public void Kernel_time_fills_darker_below_its_line()
    { var px = Paint(Steady(80, 30)); // total line at y 15, kernel line at y 53
      Assert.Equal(0xD9E7F1, px[50, 65]); Assert.Equal(0xF1F6FA, px[50, 35]);
      Assert.Equal(0xBAD6E8, px[50, 68]); Assert.Equal(0xCEE2F0, px[50, 45]); Assert.Equal(0xD9EAF4, px[50, 8]); }

    [Fact] public void Vertical_grid_lines_sit_on_every_tenth_point()
    { var px = Paint(Steady(0, 0)); // sequences 0..59, newest at x 121, dx = 121/59
      Assert.Equal(0xD9EAF4, px[62, 5]); Assert.Equal(0xD9EAF4, px[82, 5]); Assert.Equal(0xFFFFFF, px[60, 5]); }

    [Fact] public void Grid_scrolls_with_the_data()
    { var h = Steady(0, 0); h.Add(61 * Second, [0f, 0f]); // one more point: every line moves one dx left
      var px = Paint(h);
      Assert.Equal(0xD9EAF4, px[59, 5]); Assert.Equal(0xD9EAF4, px[80, 5]); Assert.Equal(0xFFFFFF, px[62, 5]); }

    // F5 held for 5 s after a minute at Normal speed, every 50 ms, while the timer still ticks each second. The refreshes are
    // not beats, so the lines stay 10 s apart, at 51 s and 61 s, rather than one every 10 refreshes.
    [Fact] public void Quick_refreshes_add_no_grid_lines()
    { var h = Steady(0, 0); var beats = Timed(h); // beats at 1..60 s
      for (var k = 1; k <= 100; k++)
      { var t = 60 * Second + k * 500_000; h.Add(t, [0f, 0f]); beats.Note(t, timed: k % 20 == 0, Second); }
      var px = Paint(h, second: -1, width: 600, beats: beats); // newest at 65 s and x 599, 10.15 px a second
      Assert.Equal(0xD9EAF4, px[457, 5]); Assert.Equal(0xD9EAF4, px[558, 5]); // 51 s and 61 s
      for (var x = 458; x < 599; x++) if (x != 558) Assert.Equal(0xFFFFFF, px[x, 5]); }

    // High speed's 0.5-s ticks never fold, so its ring fills to History.Capacity points, and every one is painted.
    [Fact] public void A_full_ring_at_high_speed_paints_border_to_border()
    { var h = new History(2); for (var i = 0; i < History.Capacity + 9; i++) h.Add((i + 1) * 5_000_000L, [33f, 0f]);
      var px = Paint(h, second: -1, width: 600);
      Assert.Equal(History.Capacity, h.Count); Assert.Equal(0x117DBB, px[1, 50]); Assert.Equal(0x117DBB, px[598, 50]); }

    // Only rows without beats, as for a pane the view has not handed them to.
    [Fact] public void No_beats_draw_only_the_grid_rows()
    { var px = Paint(Steady(0, 0), beats: new GridBeats());
      Assert.Equal(0xFFFFFF, px[62, 5]); Assert.Equal(0xD9EAF4, px[62, 8]); }

    // GetGuiResources sees GDI and USER handles only; the allocation count is what catches a GDI+ object (a Matrix, a Region)
    // created per paint.
    [Fact] public void Painting_creates_no_gdi_or_user_objects()
    { var h = Steady(70, 20); var beats = Timed(h); using var bmp = new Bitmap(122, 76); using var g = Graphics.FromImage(bmp);
      using var p = new GraphPainter(GraphPalettes.Cpu); p.Paint(g, new Rectangle(0, 0, 122, 76), h, 0, 100, 10, beats, 1);
      var gdi = GuiResources(0); var user = GuiResources(1);
      var bytes = GC.GetAllocatedBytesForCurrentThread();
      for (var i = 0; i < 1000; i++) p.Paint(g, new Rectangle(0, 0, 122, 76), h, 0, 100, 10, beats, 1);
      var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
      Assert.Equal(gdi, GuiResources(0)); Assert.Equal(user, GuiResources(1)); Assert.Equal(0, allocated); }

    [Fact] public void Tiny_bounds_paint_nothing_and_do_not_throw()
    { using var bmp = new Bitmap(122, 76); using var g = Graphics.FromImage(bmp); g.Clear(Color.White);
      using var p = new GraphPainter(GraphPalettes.Cpu); var h = Steady(70, 20); var beats = Timed(h);
      foreach (var bounds in new[] { new Rectangle(0, 0, 2, 76), new Rectangle(0, 0, 122, 2), Rectangle.Empty, new Rectangle(10, 10, -5, -5) })
          p.Paint(g, bounds, h, 0, 100, 10, beats, 1);
      for (var x = 0; x < 122; x++) for (var y = 0; y < 76; y++) Assert.Equal(0xFFFFFF, bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF); }

    // NaN is 0, not max and not a skipped point.
    [Fact] public void Nan_values_paint_as_zero()
    { var h = Steady(50, 0); h.Add(61 * Second, [float.NaN, float.NaN]); // the newest point drops to 0
      var px = Paint(h);
      Assert.Equal(0xF1F6FA, px[50, 70]); Assert.Equal(0xFFFFFF, px[50, 20]); // the half-load fill is still drawn
      // 600 wide, so the last segment spans 10 px: it falls from (589, 38) to the bottom at x 599, crossing x 594 at
      // y 56.5, and both probes are over 2 px from it.
      var wide = Paint(h, second: -1, width: 600);
      Assert.Equal(0xF1F6FA, wide[594, 70]); // filled below it: a skipped point would end the fill at x 589
      Assert.Equal(0xFFFFFF, wide[594, 48]); // white above it: a NaN taken as max would rise to the top
      var nan = new History(2); for (var i = 0; i < 60; i++) nan.Add((i + 1) * Second, [float.NaN, float.NaN]);
      var all = Paint(nan); // white apart from the grid
      Assert.Equal(0xFFFFFF, all[50, 20]); Assert.Equal(0xFFFFFF, all[50, 70]); Assert.Equal(0xD9EAF4, all[50, 68]); }

    [Fact] public void Paint_puts_back_the_callers_clip_modes_and_transform()
    { using var bmp = new Bitmap(200, 120); using var g = Graphics.FromImage(bmp);
      using var p = new GraphPainter(GraphPalettes.Cpu); var h = Steady(70, 20); var beats = Timed(h);
      g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.Half;
      g.TranslateTransform(7, 5); g.SetClip(new Rectangle(3, 2, 150, 100));
      var clip = g.ClipBounds; var transform = g.TransformElements;
      p.Paint(g, new Rectangle(10, 10, 122, 76), h, 0, 100, 10, beats, 1);
      Assert.Equal(clip, g.ClipBounds); Assert.Equal(transform, g.TransformElements);
      Assert.Equal(SmoothingMode.AntiAlias, g.SmoothingMode); Assert.Equal(PixelOffsetMode.Half, g.PixelOffsetMode);

      g.ResetClip(); // infinite comes back infinite, not as a huge rectangle
      p.Paint(g, new Rectangle(10, 10, 122, 76), h, 0, 100, 10, beats, 1);
      using var region = g.Clip;
      Assert.True(region.IsInfinite(g)); }
}
