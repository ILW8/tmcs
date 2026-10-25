using System.Globalization;
using Tmcs.Devices;
using Tmcs.Devices.Cpu;
using Tmcs.Devices.Gpu;
using Tmcs.Native;
using Tmcs.Performance;
using Tmcs.Performance.Views;
using Tmcs.Processes;
using Tmcs.Settings;
using Tmcs.Tests.Devices;

namespace Tmcs.Tests.Performance;

public sealed class GpuPaneTests
{
    const long Second = 10_000_000;
    const long Gib = 1L << 30;

    static uint GuiResources(uint flags) => User32.GetGuiResources(Kernel32.GetCurrentProcess(), flags);

    /// <summary>The owner's RTX 3090: 24.0 GB dedicated, 31.8 GB shared (31.825 GiB) and 254 MB hardware reserved.</summary>
    static GpuInfo Rtx3090() => new(
        0, 0x1D8C7, "NVIDIA GeForce RTX 3090",
        [GpuEngine.ThreeD, GpuEngine.Other, GpuEngine.VideoDecode, GpuEngine.Copy, GpuEngine.Copy, GpuEngine.Other, GpuEngine.VideoEncode],
        24 * Gib, 34_172_811_264, 254L << 20, "32.0.16.1088", "7/22/2026", "12 (FL 12.1)", "PCI bus 1, device 0, function 0");

    /// <summary>14% busy, 3.0 GB dedicated and 0.3 GB shared in use, at <paramref name="temperature"/> °C.</summary>
    static GpuCounters Busy(float? temperature = 39.2f) => new(14, 10, 1.5f, 2, 12, 3 * Gib, 322_122_547, temperature);

    /// <summary>A GPU pane at Task Manager's pane size and 96 DPI, showing a fake RTX 3090.</summary>
    sealed class Rig : IDisposable
    {
        public readonly PaneFonts Fonts = new(96);
        public readonly GpuDevice Gpu = new(Rtx3090());
        public readonly GpuPane Pane;
        long _time;

        public Rig() => Pane = new GpuPane(Fonts) { ClientSize = new Size(1052, 575), Device = Gpu };

        /// <summary><paramref name="count"/> samples of <paramref name="counters"/> one second apart: 61 fill a graph.</summary>
        public void Sample(int count, GpuCounters counters)
        {
            for (var i = 0; i < count; i++)
            {
                _time += Second;
                Gpu.Accept(counters, _time);
            }
        }

        public void Dispose()
        {
            Pane.Dispose();
            Fonts.Dispose();
        }
    }

    /// <summary>Shows the rig's pane in an off-screen form, so that its scrollbar is real.</summary>
    static OffscreenForm Host(Rig rig, int height)
    {
        var form = new OffscreenForm { ClientSize = new Size(1052, height) };
        rig.Pane.Bounds = new Rectangle(0, 0, 1052, height);
        form.Controls.Add(rig.Pane);
        form.ShowOffscreen();
        return form;
    }

    /// <summary>The pane's pixel at (<paramref name="x"/>, <paramref name="y"/>): a 1-px paint, as each text run on a bitmap's Graphics copies the bitmap.</summary>
    static int Rgb(GpuPane pane, int x, int y)
    {
        using var bmp = new Bitmap(1, 1);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TranslateTransform(-x, -y);
            pane.PaintTo(g);
        }

        return bmp.GetPixel(0, 0).ToArgb() & 0xFFFFFF;
    }

    /// <summary>Task Manager's layout with the scrollbar showing, at 575 px tall and at 257 alike (look reference sections 4, 7 and 8).</summary>
    static void AssertTaskManagersLayout(GpuPane pane)
    {
        Assert.True(pane.VerticalScroll.Visible);
        Assert.Equal(1011, pane.ContentRight); // the scrollbar narrows the client by 17
        Assert.Equal(
            [new Rectangle(23, 77, 488, 73), new Rectangle(519, 77, 488, 73), new Rectangle(23, 178, 488, 74), new Rectangle(519, 178, 488, 74)],
            Enumerable.Range(0, 4).Select(pane.EngineBounds));
        Assert.Equal(new Rectangle(21, 279, 988, 67), pane.DedicatedBounds);
        Assert.Equal(new Rectangle(21, 366, 988, 67), pane.SharedBounds);
        Assert.Equal(450, pane.InfoTop());
    }

    [Fact]
    public void Rectangles_match_task_manager() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Sample(1, Busy());
        using var form = Host(rig, 575);

        Assert.Equal(615, rig.Pane.ContentHeight);
        AssertTaskManagersLayout(rig.Pane);
        Assert.Equal(450, rig.Pane.InfoBottom - rig.Pane.BuildInfo().Height); // the block ends 20 px above the content's end
    });

    [Fact]
    public void Short_pane_keeps_the_fixed_layout() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Sample(1, Busy());
        using var form = Host(rig, 257);

        AssertTaskManagersLayout(rig.Pane);
    });

    [Fact]
    public void Info_values() => Sta.Run(() =>
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); // this test's own thread
        using var rig = new Rig();
        rig.Sample(1, Busy());

        var info = rig.Pane.BuildInfo();

        Assert.Equal(["Utilization", "GPU Memory", "Dedicated GPU memory", "Shared GPU memory", "GPU Temperature"],
            info.Items.Select(i => i.Item.Label));
        Assert.Equal(["14%", "3.3/55.8 GB", "3.0/24.0 GB", "0.3/31.8 GB", "39 °C"], info.Items.Select(i => i.Item.Value));
        Assert.Equal([0, 51, 0, 51, 102], info.Items.Select(i => i.Y)); // two columns
        Assert.Equal(info.Items[0].Width, info.Items[1].Width); // a column's items share its width
        Assert.Equal(info.Items[0].Width + 20, info.Items[2].X);
        Assert.All(info.Items, i => Assert.Equal(InfoMarker.None, i.Item.Marker));
        Assert.Equal(
            [
                ("Driver version:", "32.0.16.1088"), ("Driver date:", "7/22/2026"), ("DirectX version:", "12 (FL 12.1)"),
                ("Physical location:", "PCI bus 1, device 0, function 0"), ("Hardware reserved memory:", "254 MB"),
            ],
            info.Table.Select(r => (r.Row.Key, r.Row.Value)));
        Assert.Equal(145, info.Height);

        rig.Sample(1, Busy(temperature: null));
        info = rig.Pane.BuildInfo();

        Assert.Equal(["Utilization", "GPU Memory", "Dedicated GPU memory", "Shared GPU memory"], info.Items.Select(i => i.Item.Label));
        Assert.Equal(94, info.Height);
        Assert.Equal(450, rig.Pane.InfoTop()); // the shorter block keeps its place in the fixed layout
        Assert.Equal(615, rig.Pane.ContentHeight);
    });

    [Fact]
    public void Each_graph_draws_its_own_series() => Sta.Run(() =>
    {
        using var rig = new Rig();
        // 3D and Video Encode at 80%, Copy and Video Decode idle; dedicated memory 80% full, shared empty.
        rig.Sample(61, new GpuCounters(80, 80, 0, 80, 0, (long)(0.8 * 24 * Gib), 0, null));
        var pane = rig.Pane;
        int[] filled = [0xF1F6FA, 0xCEE2F0]; // the fill, or a grid line in it
        int[] empty = [0xFFFFFF, 0xD9EAF4]; // white, or a grid line

        // 50 px in and 10 px up from each graph's bottom: under the 80% line.
        int Inside(Rectangle graph) => Rgb(pane, graph.X + 50, graph.Bottom - 10);
        Assert.Contains(Inside(pane.EngineBounds(0)), filled);
        Assert.Contains(Inside(pane.EngineBounds(1)), empty);
        Assert.Contains(Inside(pane.EngineBounds(2)), filled);
        Assert.Contains(Inside(pane.EngineBounds(3)), empty);
        Assert.Contains(Inside(pane.DedicatedBounds), filled);
        Assert.Contains(Inside(pane.SharedBounds), empty);
    });

    [Fact]
    public void Scrolling_moves_the_content() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Sample(61, Busy());
        using var form = Host(rig, 575);
        var pane = rig.Pane;

        // x 21 is the dedicated graph's left border, from content y 279 to 345. Content y 260 is white, above its caption at
        // 263, and 346 is the white row between the graph and the shared graph's caption at 350.
        Assert.Equal(0xFFFFFF, Rgb(pane, 21, 300 - 40));
        Assert.Equal(0x117DBB, Rgb(pane, 21, 306));

        pane.AutoScrollPosition = new Point(0, 40);
        Assert.Equal(-40, pane.AutoScrollPosition.Y);

        Assert.Equal(0x117DBB, Rgb(pane, 21, 300 - 40)); // content y 300
        Assert.Equal(0xFFFFFF, Rgb(pane, 21, 306)); // content y 346: the graph's bottom moved up with it
    });

    [Fact]
    public void Without_a_gpu_nothing_paints() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Sample(61, Busy());
        // Below the title and subtitle, which a foreign device still has.
        var area = new Rectangle(0, 50, 1052, 525);

        foreach (var device in new Device?[] { null, new CpuDevice(new FakeCpuSource(new CpuInfo("c", 1, 1, 1, 1, "", 0, 0, 0))) })
        {
            rig.Pane.Device = device;
            using (var bmp = new Bitmap(area.Width, area.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.TranslateTransform(-area.X, -area.Y);
                    rig.Pane.PaintTo(g);
                }

                Assert.All(Enumerable.Range(0, area.Width * area.Height),
                    i => Assert.Equal(0xFFFFFF, bmp.GetPixel(i % area.Width, i / area.Width).ToArgb() & 0xFFFFFF));
            }

            Assert.Equal(Rectangle.Empty, rig.Pane.EngineBounds(0));
            Assert.Equal(Rectangle.Empty, rig.Pane.DedicatedBounds);
            Assert.Equal(Rectangle.Empty, rig.Pane.SharedBounds);
            Assert.Equal(0, rig.Pane.BuildInfo().Height);
        }
    });

    [Fact]
    public void The_view_shows_gpus_in_this_pane() => Sta.Run(() =>
    {
        using var model = new PerformanceModel(FakeDeviceSources.Create());
        model.Sample(Second, Array.Empty<RawProcess>());
        using var view = new PerformanceView(model, AppSettings.CreateDefault());

        Assert.True(view.Sidebar.SelectFirst(DeviceKind.Gpu));

        Assert.IsType<GpuPane>(view.VisiblePane);
        Assert.Same(model.Devices.Single(d => d.Kind == DeviceKind.Gpu), view.VisiblePane.Device);
    });

    [Fact]
    public void Painting_creates_no_gdi_or_user_objects() => Sta.Run(() =>
    {
        using var rig = new Rig();
        // Load on every engine and both memories, so every fill, line and grid in a fill runs.
        rig.Sample(61, new GpuCounters(60, 50, 20, 40, 30, 12 * Gib, 8 * Gib, 39.2f));
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
