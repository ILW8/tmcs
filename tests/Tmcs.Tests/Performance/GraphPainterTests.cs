using System.Drawing;
using Tmcs.Native;
using Tmcs.Performance;
using Tmcs.Performance.Graphs;

namespace Tmcs.Tests.Performance;

public class GraphPainterTests
{
    static History Steady(float total, float kernel)
    { var h = new History(2); for (var i = 0; i < 60; i++) h.Add((i + 1) * 10_000_000L, [total, kernel]); return h; }

    static int[,] Paint(History h, int gridSamples = 10, int second = 1)
    { using var bmp = new Bitmap(122, 76); using var g = Graphics.FromImage(bmp);
      using var p = new GraphPainter(GraphPalettes.Cpu); p.Paint(g, new Rectangle(0, 0, 122, 76), h, 0, 100, gridSamples, second);
      var px = new int[122, 76]; for (var x = 0; x < 122; x++) for (var y = 0; y < 76; y++) px[x, y] = bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF;
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

    [Fact] public void Painting_creates_no_gdi_or_user_objects()
    { var h = Steady(70, 20); using var bmp = new Bitmap(122, 76); using var g = Graphics.FromImage(bmp);
      using var p = new GraphPainter(GraphPalettes.Cpu); p.Paint(g, new Rectangle(0, 0, 122, 76), h, 0, 100, 10, 1);
      var gdi = GuiResources(0); var user = GuiResources(1);
      for (var i = 0; i < 1000; i++) p.Paint(g, new Rectangle(0, 0, 122, 76), h, 0, 100, 10, 1);
      Assert.Equal(gdi, GuiResources(0)); Assert.Equal(user, GuiResources(1)); }
}
