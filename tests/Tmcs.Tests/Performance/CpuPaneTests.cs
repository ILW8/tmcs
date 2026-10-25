using System.Globalization;
using Tmcs.Devices;
using Tmcs.Devices.Cpu;
using Tmcs.Devices.Memory;
using Tmcs.Native;
using Tmcs.Performance.Views;
using Tmcs.Processes;
using Tmcs.Tests.Devices;

namespace Tmcs.Tests.Performance;

public sealed class CpuPaneTests
{
    const long Second = 10_000_000;

    static uint GuiResources(uint flags) => User32.GetGuiResources(Kernel32.GetCurrentProcess(), flags);

    /// <summary>The owner's Ryzen 9 7950X as capture 01 shows it, with <paramref name="processors"/> logical processors.</summary>
    static CpuInfo Ryzen(int processors = 32) =>
        new("AMD Ryzen 9 7950X 16-Core Processor ", 4500, 1, 16, processors, "Enabled", 1L << 20, 16L << 20, 64L << 20);

    /// <summary>A CPU pane at Task Manager's pane size and 96 DPI, showing a fake CPU.</summary>
    sealed class Rig : IDisposable
    {
        public readonly PaneFonts Fonts = new(96);
        public readonly FakeCpuSource Source;
        public readonly CpuDevice Cpu;
        public readonly CpuPane Pane;
        long _time;

        public Rig(int processors = 32)
        {
            Source = new FakeCpuSource(Ryzen(processors));
            Cpu = new CpuDevice(Source);
            Pane = new CpuPane(Fonts) { ClientSize = new Size(1052, 575), Device = Cpu };
        }

        /// <summary><paramref name="count"/> samples one second apart: 61 fill a graph from border to border.</summary>
        public void Sample(int count, IReadOnlyList<RawProcess>? snapshot = null)
        {
            for (var i = 0; i < count; i++)
            {
                _time += Second;
                Cpu.Sample(_time, snapshot ?? []);
            }
        }

        public void Dispose()
        {
            Pane.Dispose();
            Fonts.Dispose();
        }
    }

    /// <summary>
    /// Capture 01's values, but for the utilization (25.4) and speed (98% of 4500 MHz): 481 processes with 11654 threads and
    /// 329969 handles between them, up 25 d 17:28:41.
    /// </summary>
    static Rig CaptureRig()
    {
        var rig = new Rig();
        rig.Source.Total = 25.4;
        rig.Source.Performance = 98;
        rig.Source.Up = new TimeSpan(25, 17, 28, 41);
        RawProcess Process(int threads, int handles) => new(1, 0, 0, "p.exe", 0, 0, 0, 0, threads, handles, 1, 8, false);
        rig.Sample(1, [Process(11654 - 480, 329969 - 480), .. Enumerable.Repeat(Process(1, 1), 480)]);
        return rig;
    }

    [Fact]
    public void Cells_match_task_manager() => Sta.Run(() =>
    {
        using var rig = new Rig();
        Assert.Equal(1028, rig.Pane.ContentRight); // no scrollbar narrows the pane

        Assert.Equal(new Rectangle(21, 69, 122, 76), rig.Pane.CellBounds(0));
        Assert.Equal(new Rectangle(903, 69, 123, 76), rig.Pane.CellBounds(7));
        Assert.Equal(new Rectangle(21, 309, 122, 76), rig.Pane.CellBounds(24));
        Assert.Equal(new Rectangle(903, 309, 123, 76), rig.Pane.CellBounds(31));
    });

    [Fact]
    public void Grid_shape_for_other_counts() => Sta.Run(() =>
    {
        using var rig = new Rig();
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);

        // One pane for every count: the grid follows the device it shows, as it changes.
        foreach (var (processors, columns, rows) in new[] { (16, 6, 3), (8, 4, 2), (1, 1, 1), (32, 8, 4) })
        {
            rig.Pane.Device = new CpuDevice(new FakeCpuSource(Ryzen(processors)));
            rig.Pane.PaintTo(g);

            var cells = Enumerable.Range(0, processors).Select(rig.Pane.CellBounds).ToArray();
            var xs = cells.Select(c => c.X).Distinct().Order().ToArray();
            var ys = cells.Select(c => c.Y).Distinct().Order().ToArray();
            Assert.Equal(columns, xs.Length);
            Assert.Equal(rows, ys.Length);
            // Row-major from the top left, and the last column and row reach the grid's right and bottom edges.
            for (var i = 0; i < processors; i++)
                Assert.Equal((i % columns, i / columns), (Array.IndexOf(xs, cells[i].X), Array.IndexOf(ys, cells[i].Y)));
            Assert.Equal(new Point(21, 69), cells[0].Location);
            Assert.Equal(1026, cells[columns - 1].Right);
            Assert.Equal(385, cells[^1].Bottom);
        }
    });

    [Fact]
    public void Info_starts_where_task_managers_does() => Sta.Run(() =>
    {
        using var rig = CaptureRig();

        var info = rig.Pane.BuildInfo();

        Assert.Equal(555, rig.Pane.InfoBottom);
        Assert.Equal(145, info.Height);
        Assert.Equal(410, rig.Pane.InfoBottom - info.Height);
        // Capture 01's columns less the block's x of 253: Speed at 326, Threads at 324 and Handles at 399; the table at 477 and 591.
        Assert.Equal([0, 73, 0, 71, 146, 0], info.Items.Select(i => i.X));
        Assert.All(info.Table, r => Assert.Equal((224, 338), (r.KeyX, r.ValueX)));
    });

    [Fact]
    public void Info_uses_task_managers_formats() => Sta.Run(() =>
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); // this test's own thread
        using var rig = CaptureRig();

        var info = rig.Pane.BuildInfo();

        Assert.Equal(["Utilization", "Speed", "Processes", "Threads", "Handles", "Up time"], info.Items.Select(i => i.Item.Label));
        Assert.Equal(["25%", "4.41 GHz", "481", "11654", "329969", "25:17:28:41"], info.Items.Select(i => i.Item.Value));
        Assert.Equal([0, 0, 51, 51, 51, 102], info.Items.Select(i => i.Y)); // three rows
        Assert.All(info.Items, i => Assert.Equal(InfoMarker.None, i.Item.Marker));
        Assert.Equal(8, info.Table.Count);
        Assert.Equal(
            [
                ("Base speed:", "4.50 GHz"), ("Sockets:", "1"), ("Cores:", "16"), ("Logical processors:", "32"),
                ("Virtualization:", "Enabled"), ("L1 cache:", "1.0 MB"), ("L2 cache:", "16.0 MB"), ("L3 cache:", "64.0 MB"),
            ],
            info.Table.Select(r => (r.Row.Key, r.Row.Value)));
    });

    [Fact]
    public void Each_cell_draws_its_processor_and_kernel_time() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Source.Loads[5] = new CpuLoad(80, 30);
        rig.Sample(61);
        var five = rig.Pane.CellBounds(5);
        var six = rig.Pane.CellBounds(6);
        // Cells 5 and 6 only: each text run on a bitmap's Graphics copies the bitmap.
        using var bmp = new Bitmap(six.Right - five.X, five.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TranslateTransform(-five.X, -five.Y);
            rig.Pane.PaintTo(g);
        }

        int Px(Rectangle cell, int dx, int dy) => bmp.GetPixel(cell.X - five.X + dx, cell.Y - five.Y + dy).ToArgb() & 0xFFFFFF;

        // 50 px in, clear of the vertical grid lines (18, 39, 59, …); 66 and 41 px down, clear of the rows (8, 15, 23, …). The
        // kernel line is 52.5 px down, the total's 15.
        Assert.Equal(0xD9E7F1, Px(five, 50, 66)); // the kernel fill
        Assert.Equal(0xF1F6FA, Px(five, 50, 41)); // the total's fill, above the kernel line
        Assert.Equal(0xFFFFFF, Px(six, 50, 66));
        Assert.Equal(0xFFFFFF, Px(six, 50, 41));
    });

    [Fact]
    public void Without_a_cpu_no_graph_or_info_paints() => Sta.Run(() =>
    {
        using var rig = new Rig();
        var cell = rig.Pane.CellBounds(0);
        using var bmp = new Bitmap(cell.Width, cell.Height);

        foreach (var device in new Device?[] { null, new MemoryDevice(new FakeMemorySource()) })
        {
            rig.Pane.Device = device;
            using (var g = Graphics.FromImage(bmp))
            {
                g.TranslateTransform(-cell.X, -cell.Y);
                rig.Pane.PaintTo(g);
            }

            Assert.All(Enumerable.Range(0, cell.Width * cell.Height),
                i => Assert.Equal(0xFFFFFF, bmp.GetPixel(i % cell.Width, i / cell.Width).ToArgb() & 0xFFFFFF));
            Assert.Equal(0, rig.Pane.BuildInfo().Height);
            Assert.Equal(Rectangle.Empty, rig.Pane.CellBounds(0));
        }
    });

    [Fact]
    public void Painting_creates_no_gdi_or_user_objects() => Sta.Run(() =>
    {
        using var rig = new Rig();
        // Load on every processor, the kernel's under the total's, so every fill, line and grid in a fill runs.
        for (var i = 0; i < 32; i++) rig.Source.Loads[i] = new CpuLoad(40 + i, 10 + i / 2f);
        rig.Sample(61);
        // A buffer on a memory DC, as OnPaint gets: text drawn on a bitmap's Graphics copies the bitmap for every run.
        using var bmp = new Bitmap(1, 1);
        using var target = Graphics.FromImage(bmp);
        using var context = new BufferedGraphicsContext();
        using var buffer = context.Allocate(target, rig.Pane.ClientRectangle);
        var g = buffer.Graphics;
        rig.Pane.PaintTo(g);
        // Objects earlier tests left to the finalizer are released here, not inside the counted paints.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var gdi = GuiResources(0);
        var user = GuiResources(1);

        for (var i = 0; i < 1000; i++) rig.Pane.PaintTo(g);

        Assert.Equal(gdi, GuiResources(0));
        Assert.Equal(user, GuiResources(1));
    });
}
