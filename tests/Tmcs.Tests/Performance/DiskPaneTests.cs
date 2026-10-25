using System.Globalization;
using Tmcs.Devices;
using Tmcs.Devices.Cpu;
using Tmcs.Devices.Disk;
using Tmcs.Native;
using Tmcs.Performance.Views;
using Tmcs.Tests.Devices;

namespace Tmcs.Tests.Performance;

public sealed class DiskPaneTests
{
    const long Second = 10_000_000;
    const long Mib = 1L << 20;

    static uint GuiResources(uint flags) => User32.GetGuiResources(Kernel32.GetCurrentProcess(), flags);

    /// <summary>A disk pane at Task Manager's pane size and 96 DPI, showing a fake 2 TB system SSD.</summary>
    sealed class Rig : IDisposable
    {
        public readonly PaneFonts Fonts = new(96);
        public readonly FakeDiskSource Source = new();
        public readonly DiskDevice Disk;
        public readonly DiskPane Pane;
        long _time = 1000 * Second;
        long _read, _written, _readTime, _idle;
        uint _ios;

        public Rig(long formatted = 1_990_000_000_000)
        {
            Disk = new DiskDevice(Source, new DiskInfo(0, "C:", "Test SSD", 2_000_000_000_000, formatted, true, true, "SSD"));
            Pane = new DiskPane(Fonts) { ClientSize = new Size(1052, 575), Device = Disk };
        }

        /// <summary>One second of counters: the given byte counts, <paramref name="active"/> percent busy, 1.6 ms a request.</summary>
        public void Sample(long read = 0, long written = 0, int active = 1)
        {
            _time += Second;
            _read += read;
            _written += written;
            _idle += Second * (100 - active) / 100;
            _ios += 10;
            _readTime += 160_000;
            Source.Counters = new DiskCounters(_read, _written, _readTime, 0, _idle, _ios, 0, _time);
            Disk.Sample(_time, []);
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

    /// <summary>1% active, 1.6 ms a request, nothing read and 1.3 MiB/s written.</summary>
    static Rig CaptureRig()
    {
        var rig = new Rig();
        rig.Sample(); // the first read has no rate
        rig.Sample(written: (long)(1.3 * Mib));
        return rig;
    }

    static Bitmap Paint(Rig rig)
    {
        var bmp = new Bitmap(1052, 575);
        using var g = Graphics.FromImage(bmp);
        rig.Pane.PaintTo(g);
        return bmp;
    }

    [Fact]
    public void Rectangles_match_task_manager() => Sta.Run(() =>
    {
        using var rig = new Rig();

        Assert.Equal(new Rectangle(19, 69, 1009, 263), rig.Pane.ActiveBounds);
        Assert.Equal(new Rectangle(19, 369, 1009, 67), rig.Pane.TransferBounds);
        Assert.Equal(461, rig.Pane.InfoBottom - rig.Pane.BuildInfo().Height);
    });

    [Fact]
    public void Short_pane_scrolls_with_task_managers_minimum_layout() => Sta.Run(() =>
    {
        using var rig = new Rig();
        using var form = Host(rig, 257);
        var pane = rig.Pane;

        Assert.True(pane.VerticalScroll.Visible);
        Assert.Equal(new Rectangle(19, 69, 992, 80), pane.ActiveBounds);
        Assert.Equal(new Rectangle(19, 186, 992, 67), pane.TransferBounds);
        Assert.Equal(278, pane.InfoBottom - pane.BuildInfo().Height);

        pane.Height = 392;
        Application.DoEvents();
        Assert.False(pane.VerticalScroll.Visible);
    });

    [Fact]
    public void Without_a_disk_device_nothing_paints() => Sta.Run(() =>
    {
        using var rig = new Rig();

        foreach (var device in new Device?[] { null, new CpuDevice(new FakeCpuSource(new CpuInfo("c", 1, 1, 1, 1, "", 0, 0, 0))) })
        {
            rig.Pane.Device = device;
            using (Paint(rig)) { }
            Assert.Equal(Rectangle.Empty, rig.Pane.ActiveBounds);
            Assert.Equal(Rectangle.Empty, rig.Pane.TransferBounds);
            Assert.Equal(0, rig.Pane.BuildInfo().Height);
        }
    });

    [Fact]
    public void Markers_sit_beside_read_and_write() => Sta.Run(() =>
    {
        using var rig = CaptureRig();
        using var bmp = Paint(rig);

        int Px(int x, int y) => bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF;
        Assert.Equal(0x4DA60C, Px(19, 515));
        Assert.Equal(0x4DA60C, Px(20, 554));
        Assert.Equal(0x79D50D, Px(107, 515));
        Assert.Equal(0xFFFFFF, Px(107, 518));
    });

    [Fact]
    public void Info_values() => Sta.Run(() =>
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); // this test's own thread
        using var rig = CaptureRig();

        var info = rig.Pane.BuildInfo();

        Assert.Equal(["Active time", "Average response time", "Read speed", "Write speed"], info.Items.Select(i => i.Item.Label));
        Assert.Equal(["1%", "1.6 ms", "0 KB/s", "1.3 MB/s"], info.Items.Select(i => i.Item.Value));
        Assert.Equal(
            [("Capacity:", "1.8 TB"), ("Formatted:", "1.8 TB"), ("System disk:", "Yes"), ("Page file:", "Yes"), ("Type:", "SSD")],
            info.Table.Select(r => (r.Row.Key, r.Row.Value)));
    });

    [Fact]
    public void Formatted_is_left_out_when_zero() => Sta.Run(() =>
    {
        using var rig = new Rig(formatted: 0);
        Assert.DoesNotContain(rig.Pane.BuildInfo().Table, r => r.Row.Key == "Formatted:");
    });

    [Fact]
    public void Scale_follows_the_visible_rates() => Sta.Run(() =>
    {
        using var rig = CaptureRig(); // 1.3 MiB/s written: the 10 MB/s step
        using var idle = new Rig();
        idle.Sample();
        idle.Sample(); // nothing moving: the 100 KB/s floor
        using var busyBmp = Paint(rig);
        using var idleBmp = Paint(idle);

        // The caption's right edge is the graph's, 16 px above it; the two scales' captions differ in width and glyphs.
        var graph = rig.Pane.TransferBounds;
        var box = new Rectangle(graph.Right - 60, graph.Y - 16, 60, 13);
        var differs = false;
        var inked = false;
        for (var y = box.Y; y < box.Bottom; y++)
            for (var x = box.X; x < box.Right; x++)
            {
                differs |= busyBmp.GetPixel(x, y) != idleBmp.GetPixel(x, y);
                inked |= (busyBmp.GetPixel(x, y).ToArgb() & 0xFFFFFF) != 0xFFFFFF;
            }

        Assert.True(inked);
        Assert.True(differs);
        const float TenMib = 10 * 1024 * 1024;
        Assert.Equal(("10 MB/s", TenMib), (rig.Pane.TransferScale.Caption, rig.Pane.TransferScale.Max));
        Assert.Equal(("100 KB/s", 100 * 1024f), (idle.Pane.TransferScale.Caption, idle.Pane.TransferScale.Max));

        using var reading = new Rig();
        reading.Sample();
        reading.Sample(read: (long)(1.3 * Mib));
        Assert.Equal(("10 MB/s", TenMib), (reading.Pane.TransferScale.Caption, reading.Pane.TransferScale.Max));

        reading.Pane.Device = null;
        Assert.Equal(default, reading.Pane.TransferScale);
    });

    [Fact]
    public void Painting_creates_no_gdi_or_user_objects() => Sta.Run(() =>
    {
        using var rig = CaptureRig();
        for (var i = 0; i < 61; i++) rig.Sample(read: Mib, written: 2 * Mib, active: 40);
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
