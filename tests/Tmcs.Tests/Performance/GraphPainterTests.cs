using System.Drawing.Drawing2D;
using Tmcs.Native;
using Tmcs.Performance;
using Tmcs.Performance.Graphs;

namespace Tmcs.Tests.Performance;

public class GraphPainterTests
{
    static History Steady(float total, float kernel)
    { var h = new History(2); for (var i = 0; i < 60; i++) h.Add((i + 1) * 10_000_000L, [total, kernel]); return h; }

    static int[,] Paint(History h, int gridSamples = 10, int second = 1, int width = 122)
    { using var bmp = new Bitmap(width, 76); using var g = Graphics.FromImage(bmp);
      using var p = new GraphPainter(GraphPalettes.Cpu); p.Paint(g, new Rectangle(0, 0, width, 76), h, 0, 100, gridSamples, second);
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

    [Fact] public void Kernel_time_fills_darker_below_its_line()
    { var px = Paint(Steady(80, 30)); // total line at y 15, kernel line at y 52.5
      Assert.Equal(0xD9E7F1, px[50, 65]); Assert.Equal(0xF1F6FA, px[50, 35]);
      Assert.Equal(0xBAD6E8, px[50, 68]); Assert.Equal(0xCEE2F0, px[50, 45]); Assert.Equal(0xD9EAF4, px[50, 8]); }

    [Fact] public void Vertical_grid_lines_sit_on_every_tenth_point()
    { var px = Paint(Steady(0, 0)); // sequences 0..59, newest at x 121, dx = 121/59
      Assert.Equal(0xD9EAF4, px[62, 5]); Assert.Equal(0xD9EAF4, px[82, 5]); Assert.Equal(0xFFFFFF, px[60, 5]); }

    [Fact] public void Grid_scrolls_with_the_data()
    { var h = Steady(0, 0); h.Add(61 * 10_000_000L, [0f, 0f]); // one more point: every line moves one dx left
      var px = Paint(h);
      Assert.Equal(0xD9EAF4, px[59, 5]); Assert.Equal(0xD9EAF4, px[80, 5]); Assert.Equal(0xFFFFFF, px[62, 5]); }

    // GetGuiResources sees GDI and USER handles only; the allocation count is what catches a GDI+ object (a Matrix, a Region)
    // created per paint.
    [Fact] public void Painting_creates_no_gdi_or_user_objects()
    { var h = Steady(70, 20); using var bmp = new Bitmap(122, 76); using var g = Graphics.FromImage(bmp);
      using var p = new GraphPainter(GraphPalettes.Cpu); p.Paint(g, new Rectangle(0, 0, 122, 76), h, 0, 100, 10, 1);
      var gdi = GuiResources(0); var user = GuiResources(1);
      var bytes = GC.GetAllocatedBytesForCurrentThread();
      for (var i = 0; i < 1000; i++) p.Paint(g, new Rectangle(0, 0, 122, 76), h, 0, 100, 10, 1);
      var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
      Assert.Equal(gdi, GuiResources(0)); Assert.Equal(user, GuiResources(1)); Assert.Equal(0, allocated); }

    [Fact] public void Tiny_bounds_paint_nothing_and_do_not_throw()
    { using var bmp = new Bitmap(122, 76); using var g = Graphics.FromImage(bmp); g.Clear(Color.White);
      using var p = new GraphPainter(GraphPalettes.Cpu); var h = Steady(70, 20);
      foreach (var bounds in new[] { new Rectangle(0, 0, 2, 76), new Rectangle(0, 0, 122, 2), Rectangle.Empty, new Rectangle(10, 10, -5, -5) })
          p.Paint(g, bounds, h, 0, 100, 10, 1);
      for (var x = 0; x < 122; x++) for (var y = 0; y < 76; y++) Assert.Equal(0xFFFFFF, bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF); }

    // NaN is 0, not max and not a skipped point.
    [Fact] public void Nan_values_paint_as_zero()
    { var h = Steady(50, 0); h.Add(61 * 10_000_000L, [float.NaN, float.NaN]); // the newest point drops to 0
      var px = Paint(h);
      Assert.Equal(0xF1F6FA, px[50, 70]); Assert.Equal(0xFFFFFF, px[50, 20]); // the half-load fill is still drawn
      // 600 wide, so the last segment spans 10 px: it falls from (588.8, 37.5) to the bottom at x 599, crossing x 594 at
      // y 56.5, and both probes are over 2 px from it.
      var wide = Paint(h, second: -1, width: 600);
      Assert.Equal(0xF1F6FA, wide[594, 70]); // filled below it: a skipped point would end the fill at x 588.8
      Assert.Equal(0xFFFFFF, wide[594, 48]); // white above it: a NaN taken as max would rise to the top
      var nan = new History(2); for (var i = 0; i < 60; i++) nan.Add((i + 1) * 10_000_000L, [float.NaN, float.NaN]);
      var all = Paint(nan); // white apart from the grid
      Assert.Equal(0xFFFFFF, all[50, 20]); Assert.Equal(0xFFFFFF, all[50, 70]); Assert.Equal(0xD9EAF4, all[50, 68]); }

    [Fact] public void Paint_puts_back_the_callers_clip_modes_and_transform()
    { using var bmp = new Bitmap(200, 120); using var g = Graphics.FromImage(bmp);
      using var p = new GraphPainter(GraphPalettes.Cpu); var h = Steady(70, 20);
      g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.Half;
      g.TranslateTransform(7, 5); g.SetClip(new Rectangle(3, 2, 150, 100));
      var clip = g.ClipBounds; var transform = g.TransformElements;
      p.Paint(g, new Rectangle(10, 10, 122, 76), h, 0, 100, 10, 1);
      Assert.Equal(clip, g.ClipBounds); Assert.Equal(transform, g.TransformElements);
      Assert.Equal(SmoothingMode.AntiAlias, g.SmoothingMode); Assert.Equal(PixelOffsetMode.Half, g.PixelOffsetMode);

      g.ResetClip(); // infinite comes back infinite, not as a huge rectangle
      p.Paint(g, new Rectangle(10, 10, 122, 76), h, 0, 100, 10, 1);
      using var region = g.Clip;
      Assert.True(region.IsInfinite(g)); }
}
