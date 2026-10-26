using System.Globalization;
using Tmcs.Devices;
using Tmcs.Devices.Cpu;
using Tmcs.Devices.Memory;
using Tmcs.Native;
using Tmcs.Performance.Views;
using Tmcs.Processes;
using Tmcs.Tests.Devices;

namespace Tmcs.Tests.Performance;

public sealed class MemoryPaneTests
{
    const long Gib = FakeMemorySource.Gib;
    const long Mib = 1L << 20;

    static uint GuiResources(uint flags) => User32.GetGuiResources(Kernel32.GetCurrentProcess(), flags);

    /// <summary>A memory pane at Task Manager's pane size and 96 DPI, showing a fake memory device.</summary>
    sealed class Rig : IDisposable
    {
        public readonly PaneFonts Fonts = new(96);
        public readonly FakeMemorySource Source = new();
        public MemoryDevice Memory;
        public readonly MemoryPane Pane;
        long _time;

        public Rig()
        {
            Memory = new MemoryDevice(Source);
            Pane = new MemoryPane(Fonts) { ClientSize = new Size(1052, 575), Device = Memory };
        }

        /// <summary>A new device, for a change to the source's <see cref="MemoryInfo"/>, which a device reads once.</summary>
        public void Reload()
        {
            Memory = new MemoryDevice(Source);
            Pane.Device = Memory;
        }

        public void Sample(int count, IReadOnlyList<RawProcess>? snapshot = null)
        {
            for (var i = 0; i < count; i++)
            {
                _time += 10_000_000;
                Memory.Sample(_time, snapshot ?? []);
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

    /// <summary>Capture 02's figures: 38.0 GiB in use, 3.6 GiB of it compressed, 54.7 of 119.7 GiB committed.</summary>
    static Rig CaptureRig()
    {
        var rig = new Rig();
        rig.Source.Counters = new MemoryCounters(
            Available: 26 * Gib, Committed: (long)(54.7 * Gib), CommitLimit: (long)(119.7 * Gib),
            PagedPool: 600 * Mib, NonPagedPool: 500 * Mib, Modified: 1 * Gib, Standby: 22 * Gib, Free: 3 * Gib);
        rig.Sample(1, [new RawProcess(1234, 4, 0, "Memory Compression", 0, 0, 0, 0, 1, 0, 0, 8, false, (long)(3.6 * Gib))]);
        return rig;
    }

    [Fact]
    public void Rectangles_match_task_manager() => Sta.Run(() =>
    {
        using var rig = new Rig();

        Assert.Equal(new Rectangle(19, 69, 1009, 226), rig.Pane.GraphBounds);
        Assert.Equal(new Rectangle(19, 334, 1009, 52), rig.Pane.BarBounds);
        Assert.Equal(316, rig.Pane.CompositionTop);
    });

    [Fact]
    public void Segments_split_the_inner_width() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Source.Info = rig.Source.Info with { TotalPhysical = (38950 + 297 + 22875 + 3058) * Mib };
        rig.Reload();
        rig.Source.Counters = new MemoryCounters(0, 0, 0, 0, 0, Modified: 297 * Mib, Standby: 22875 * Mib, Free: 3058 * Mib);
        rig.Sample(1);

        Assert.Equal(38950 * Mib, rig.Memory.InUse);
        Assert.Equal((602, 5, 353, 47), rig.Pane.SegmentWidths());
    });

    [Fact]
    public void Segments_are_empty_without_memory() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Pane.Device = null;
        Assert.Equal((0, 0, 0, 0), rig.Pane.SegmentWidths());
        Assert.Equal(Rectangle.Empty, rig.Pane.GraphBounds);
        Assert.Equal(Rectangle.Empty, rig.Pane.BarBounds);
    });

    [Fact]
    public void Info_values() => Sta.Run(() =>
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); // this test's own thread
        using var rig = CaptureRig();

        var info = rig.Pane.BuildInfo();

        Assert.Equal(
            ["In use (Compressed)", "Available", "Committed", "Cached", "Paged pool", "Non-paged pool"],
            info.Items.Select(i => i.Item.Label));
        Assert.Equal(
            ["38.0 GB (3.6 GB)", "26.0 GB", "55/120 GB", "23.0 GB", "600 MB", "500 MB"],
            info.Items.Select(i => i.Item.Value));
        Assert.Equal([0, 0, 51, 51, 102, 102], info.Items.Select(i => i.Y));
        Assert.Equal(
            [("Speed:", "5600 MHz"), ("Slots used:", "2 of 4"), ("Form factor:", "DIMM"), ("Hardware reserved:", "357 MB")],
            info.Table.Select(r => (r.Row.Key, r.Row.Value)));

        rig.Source.Info = rig.Source.Info with { Slots = 0 };
        rig.Pane.Device = new MemoryDevice(rig.Source);
        Assert.DoesNotContain(rig.Pane.BuildInfo().Table, r => r.Row.Key == "Slots used:");
    });

    [Fact]
    public void Info_starts_where_task_managers_does() => Sta.Run(() =>
    {
        using var rig = CaptureRig();

        var info = rig.Pane.BuildInfo();

        Assert.Equal(145, info.Height);
        Assert.Equal(410, rig.Pane.InfoBottom - info.Height);
    });

    [Fact]
    public void Short_pane_scrolls_with_task_managers_minimum_layout() => Sta.Run(() =>
    {
        using var rig = new Rig();
        using var form = Host(rig, 257);
        var pane = rig.Pane;

        Assert.True(pane.VerticalScroll.Visible);
        Assert.Equal(new Rectangle(19, 69, 992, 80), pane.GraphBounds);
        Assert.Equal(170, pane.CompositionTop);
        Assert.Equal(new Rectangle(19, 188, 992, 52), pane.BarBounds);
        Assert.Equal(264, pane.InfoBottom - pane.BuildInfo().Height);

        pane.Height = 429;
        Application.DoEvents();
        Assert.False(pane.VerticalScroll.Visible);
    });

    [Fact]
    public void Without_a_memory_device_nothing_paints() => Sta.Run(() =>
    {
        using var rig = new Rig();
        using var bmp = new Bitmap(1052, 575);

        foreach (var device in new Device?[] { null, new CpuDevice(new FakeCpuSource(new CpuInfo("c", 1, 1, 1, 1, "", 0, 0, 0))) })
        {
            rig.Pane.Device = device;
            using (var g = Graphics.FromImage(bmp)) rig.Pane.PaintTo(g);
            Assert.Equal(Rectangle.Empty, rig.Pane.GraphBounds);
            Assert.Equal(0, rig.Pane.BuildInfo().Height);
        }
    });

    [Fact]
    public void Bar_draws_its_segments() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Source.Info = rig.Source.Info with { TotalPhysical = (38950 + 297 + 22875 + 3058) * Mib };
        rig.Reload();
        rig.Source.Counters = new MemoryCounters(0, 0, 0, 0, 0, Modified: 297 * Mib, Standby: 22875 * Mib, Free: 3058 * Mib);
        rig.Sample(1);
        using var bmp = new Bitmap(1052, 575);
        using (var g = Graphics.FromImage(bmp)) rig.Pane.PaintTo(g);

        int Px(int x, int y) => bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF;
        var bar = rig.Pane.BarBounds;
        var y = bar.Y + 20;
        Assert.Equal(0x8B12AE, Px(bar.X, y));
        Assert.Equal(0x8B12AE, Px(bar.Right - 1, y));
        Assert.Equal(0xF4F2F4, Px(bar.X + 1, y));
        Assert.Equal(0xCEB0D7, Px(bar.X + 1 + 602, y));
        Assert.Equal(0xEEE6F1, Px(bar.X + 1 + 603, y));
        Assert.Equal(0xCEB0D7, Px(bar.X + 1 + 607, y));
        Assert.Equal(0xFFFFFF, Px(bar.X + 1 + 608, y));
        Assert.Equal(0xCEB0D7, Px(bar.X + 1 + 960, y));
        Assert.Equal(0xFFFFFF, Px(bar.Right - 2, y));
    });

    static MemoryCounters Counters(long modified, long standby, long free) =>
        new(0, 0, 0, 0, 0, Modified: modified, Standby: standby, Free: free);

    [Fact]
    public void Segments_never_pass_the_inner_width() => Sta.Run(() =>
    {
        using var rig = new Rig();
        // Thirds with no Free: 1007 / 3 rounds up three times, to 1008.
        rig.Source.Info = rig.Source.Info with { TotalPhysical = 3 * Gib };
        rig.Source.Counters = Counters(Gib, Gib, 0);
        rig.Reload();
        rig.Sample(1);

        var (a, b, c, d) = rig.Pane.SegmentWidths();
        Assert.Equal((336, 336, 335, 0), (a, b, c, d));
        Assert.Equal(1007, a + b + c + d);

        using var bmp = new Bitmap(1052, 575);
        using (var g = Graphics.FromImage(bmp)) rig.Pane.PaintTo(g);
        var bar = rig.Pane.BarBounds;
        Assert.Equal(0x8B12AE, bmp.GetPixel(bar.Right - 1, bar.Y + 20).ToArgb() & 0xFFFFFF);
    });

    [Fact]
    public void Negative_in_use_and_zero_total_are_safe() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Source.Info = rig.Source.Info with { TotalPhysical = Gib };
        rig.Source.Counters = Counters(0, 2 * Gib, 0); // In use = 1 - 2 GiB
        rig.Reload();
        rig.Sample(1);
        Assert.True(rig.Memory.InUse < 0);
        Assert.Equal((0, 0, 1007, 0), rig.Pane.SegmentWidths());

        rig.Source.Info = rig.Source.Info with { TotalPhysical = 0 };
        rig.Reload();
        rig.Sample(1);
        Assert.Equal((0, 0, 0, 1007), rig.Pane.SegmentWidths());
        using var bmp = new Bitmap(1052, 575);
        using var g = Graphics.FromImage(bmp);
        rig.Pane.PaintTo(g);
    });

    [Fact]
    public void A_zero_width_in_use_leaves_no_separator_at_the_border() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Source.Info = rig.Source.Info with { TotalPhysical = 10 * Gib };
        rig.Source.Counters = Counters(0, 4 * Gib, 6 * Gib); // all of it Standby and Free
        rig.Reload();
        rig.Sample(1);
        using var bmp = new Bitmap(1052, 575);
        using (var g = Graphics.FromImage(bmp)) rig.Pane.PaintTo(g);

        var bar = rig.Pane.BarBounds;
        Assert.Equal((0, 0, 403, 604), rig.Pane.SegmentWidths());
        Assert.Equal(0xFFFFFF, bmp.GetPixel(bar.X + 1, bar.Y + 20).ToArgb() & 0xFFFFFF); // Standby, first drawn
        Assert.Equal(0xCEB0D7, bmp.GetPixel(bar.X + 1 + 403, bar.Y + 20).ToArgb() & 0xFFFFFF);
    });

    [Fact]
    public void Unknown_smbios_rows_are_left_out() => Sta.Run(() =>
    {
        using var rig = new Rig();
        var keys = (MemoryInfo info) =>
        {
            rig.Source.Info = info;
            rig.Reload();
            return rig.Pane.BuildInfo().Table.Select(r => r.Row.Key).ToArray();
        };
        var full = rig.Source.Info;

        Assert.Equal(["Slots used:", "Form factor:", "Hardware reserved:"], keys(full with { SpeedMhz = 0 }));
        Assert.Equal(["Speed:", "Slots used:", "Hardware reserved:"], keys(full with { FormFactor = " " }));
        Assert.Equal(["Speed:", "Slots used:", "Form factor:"], keys(full with { Installed = full.TotalPhysical }));
    });

    [Fact]
    public void Painting_creates_no_gdi_or_user_objects() => Sta.Run(() =>
    {
        using var rig = CaptureRig();
        rig.Sample(61);
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
