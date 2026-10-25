using System.Globalization;
using Tmcs.Devices;
using Tmcs.Devices.Cpu;
using Tmcs.Devices.Network;
using Tmcs.Native;
using Tmcs.Performance.Views;
using Tmcs.Tests.Devices;

namespace Tmcs.Tests.Performance;

public sealed class NetworkPaneTests
{
    const long Second = 10_000_000;

    /// <summary>A second of this many bytes received reads <c>40.0 Kbps</c>, capture 10's value 86 px wide.</summary>
    const ulong FortyKbps = 5000;

    static uint GuiResources(uint flags) => User32.GetGuiResources(Kernel32.GetCurrentProcess(), flags);

    /// <summary>
    /// An adapter with a fake source of its own, read once a second, with both addresses and no DNS suffix unless given one. A
    /// Wi-Fi one is on capture 12's network.
    /// </summary>
    sealed class Adapter
    {
        public readonly FakeNetworkSource Source = new() { Counters = new(0, 0, IsUp: true) };
        public readonly NetworkDevice Device;
        long _time = 1000 * Second;
        ulong _received, _sent;

        public Adapter(bool wifi, string? dns = null, string? ipv4 = "10.24.200.9", string? ipv6 = "fe80::38b8:b061:af62:4d58%76",
            bool wifiDetails = true)
        {
            if (wifi && wifiDetails) Source.Wifi = new WifiInfo("frame_4BD93CDF", "802.11ax", 60);
            Device = new NetworkDevice(Source, new AdapterInfo(Guid.NewGuid(), 7, wifi ? "Wi-Fi 2" : "Ethernet 3",
                wifi ? "Test Wi-Fi adapter" : "Test adapter", dns, wifi, ipv4, ipv6));
            Sample(); // the first read has no rate, but it brings the Wi-Fi details
        }

        /// <summary>One second in which <paramref name="received"/> and <paramref name="sent"/> bytes moved.</summary>
        public void Sample(ulong received = 0, ulong sent = 0)
        {
            _time += Second;
            _received += received;
            _sent += sent;
            Source.Counters = new AdapterCounters(_received, _sent, IsUp: true);
            Device.Sample(_time, []);
        }
    }

    /// <summary>A network pane at Task Manager's pane size and 96 DPI, showing an adapter.</summary>
    sealed class Rig : IDisposable
    {
        public readonly PaneFonts Fonts = new(96);
        public readonly NetworkPane Pane;

        public Rig(Adapter adapter) => Pane = new NetworkPane(Fonts) { ClientSize = new Size(1052, 575), Device = adapter.Device };

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

    /// <summary>The pane's pixels in <paramref name="area"/> only: each text run on a bitmap's Graphics copies the bitmap.</summary>
    static Bitmap Paint(NetworkPane pane, Rectangle area)
    {
        var bmp = new Bitmap(area.Width, area.Height);
        using var g = Graphics.FromImage(bmp);
        g.TranslateTransform(-area.X, -area.Y);
        pane.PaintTo(g);
        return bmp;
    }

    static int Rgb(Bitmap bmp, int x, int y) => bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF;

    static int InfoTop(NetworkPane pane) => pane.InfoBottom - pane.BuildInfo().Height;

    static string[] Keys(NetworkPane pane) => [.. pane.BuildInfo().Table.Select(r => r.Row.Key)];

    [Fact]
    public void Ethernet_rectangles() => Sta.Run(() =>
    {
        using var rig = new Rig(new Adapter(wifi: false));
        var pane = rig.Pane;

        Assert.Equal(new Rectangle(19, 69, 1009, 367), pane.GraphBounds);
        Assert.Equal(461, InfoTop(pane));

        // x 19, from the send marker's first dash at y 464 to the receive marker's top at 515.
        using var bmp = Paint(pane, new Rectangle(19, 464, 1, 52));
        Assert.Equal(0xDF672C, Rgb(bmp, 0, 0));
        Assert.Equal(0xFFFFFF, Rgb(bmp, 0, 3)); // the gap after the first 3-px dash
        Assert.Equal(0xDF672C, Rgb(bmp, 0, 6));
        Assert.Equal(0xA74F01, Rgb(bmp, 0, 51));
    });

    [Fact]
    public void Wifi_rectangles() => Sta.Run(() =>
    {
        using var rig = new Rig(new Adapter(wifi: true));
        var pane = rig.Pane;

        Assert.Equal(new Rectangle(19, 69, 1009, 352), pane.GraphBounds);
        Assert.Equal(446, InfoTop(pane));
    });

    [Fact]
    public void Content_height_follows_the_info_block() => Sta.Run(() =>
    {
        using var rig = new Rig(new Adapter(wifi: false));
        var pane = rig.Pane;
        Assert.Equal(288, pane.ContentHeight);

        pane.Device = new Adapter(wifi: true).Device;
        Assert.Equal(303, pane.ContentHeight); // six rows, the signal row 19 px: 109

        pane.Device = new Adapter(wifi: true, dns: "home.arpa").Device;
        Assert.Equal(321, pane.ContentHeight); // seven rows: 127

        pane.Device = new Adapter(wifi: true, wifiDetails: false).Device;
        Assert.Equal(288, pane.ContentHeight); // no SSID or signal row: the items' 94

        pane.Device = null;
        Assert.Equal(288, pane.ContentHeight);
    });

    [Fact]
    public void Short_pane_scrolls_with_task_managers_minimum_layout() => Sta.Run(() =>
    {
        var ethernet = new Adapter(wifi: false);
        var wifi = new Adapter(wifi: true);
        foreach (var (adapter, fits) in new[] { (ethernet, 288), (wifi, 303) })
        {
            using var rig = new Rig(adapter);
            using var form = Host(rig, 257);
            var pane = rig.Pane;

            Assert.True(pane.VerticalScroll.Visible);
            Assert.Equal(new Rectangle(19, 69, 992, 80), pane.GraphBounds);
            Assert.Equal(174, InfoTop(pane));

            pane.Height = fits;
            Application.DoEvents();
            Assert.False(pane.VerticalScroll.Visible);
        }

        // The scroll range follows the device: the same pane, tall enough for Ethernet's 288 but not for Wi-Fi's 303.
        using (var rig = new Rig(ethernet))
        using (Host(rig, 290))
        {
            Assert.False(rig.Pane.VerticalScroll.Visible);

            rig.Pane.Device = wifi.Device;
            Application.DoEvents();

            Assert.True(rig.Pane.VerticalScroll.Visible);
        }
    });

    [Fact]
    public void Info_values() => Sta.Run(() =>
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); // this test's own thread
        var ethernet = new Adapter(wifi: false, dns: "home.arpa");
        ethernet.Sample(received: FortyKbps, sent: 1000);
        using var rig = new Rig(ethernet);

        var info = rig.Pane.BuildInfo();

        Assert.Equal(["Send", "Receive"], info.Items.Select(i => i.Item.Label));
        Assert.Equal(["8.0 Kbps", "40.0 Kbps"], info.Items.Select(i => i.Item.Value));
        Assert.Equal([InfoMarker.Dashed, InfoMarker.Solid], info.Items.Select(i => i.Item.Marker));
        Assert.Equal([0, 51], info.Items.Select(i => i.Y)); // each on its own row
        Assert.Equal(
            [
                ("Adapter name:", "Ethernet 3"), ("DNS name:", "home.arpa"), ("Connection type:", "Ethernet"),
                ("IPv4 address:", "10.24.200.9"), ("IPv6 address:", "fe80::38b8:b061:af62:4d58%76"),
            ],
            info.Table.Select(r => (r.Row.Key, r.Row.Value)));

        rig.Pane.Device = new Adapter(wifi: true, dns: "home.arpa").Device;
        info = rig.Pane.BuildInfo();

        Assert.Equal(
            [
                ("Adapter name:", "Wi-Fi 2"), ("SSID:", "frame_4BD93CDF"), ("DNS name:", "home.arpa"), ("Connection type:", "802.11ax"),
                ("IPv4 address:", "10.24.200.9"), ("IPv6 address:", "fe80::38b8:b061:af62:4d58%76"), ("Signal strength:", ""),
            ],
            info.Table.Select(r => (r.Row.Key, r.Row.Value)));
        Assert.Equal([0, 18, 36, 54, 72, 90, 108], info.Table.Select(r => r.Y));
        Assert.Equal(19, info.Table[^1].Row.Height);
    });

    [Fact]
    public void Dns_row_is_left_out_when_empty() => Sta.Run(() =>
    {
        using var rig = new Rig(new Adapter(wifi: false));
        Assert.Equal(["Adapter name:", "Connection type:", "IPv4 address:", "IPv6 address:"], Keys(rig.Pane));

        rig.Pane.Device = new Adapter(wifi: false, dns: "").Device;
        Assert.Equal(["Adapter name:", "Connection type:", "IPv4 address:", "IPv6 address:"], Keys(rig.Pane));

        rig.Pane.Device = new Adapter(wifi: true).Device;
        Assert.Equal(["Adapter name:", "SSID:", "Connection type:", "IPv4 address:", "IPv6 address:", "Signal strength:"], Keys(rig.Pane));
    });

    [Fact]
    public void Missing_addresses_and_wifi_details_are_left_out() => Sta.Run(() =>
    {
        using var rig = new Rig(new Adapter(wifi: false, ipv4: null));
        Assert.Equal(["Adapter name:", "Connection type:", "IPv6 address:"], Keys(rig.Pane));

        rig.Pane.Device = new Adapter(wifi: false, ipv6: null).Device;
        Assert.Equal(["Adapter name:", "Connection type:", "IPv4 address:"], Keys(rig.Pane));

        rig.Pane.Device = new Adapter(wifi: true, wifiDetails: false).Device;
        Assert.Equal(["Adapter name:", "Connection type:", "IPv4 address:", "IPv6 address:"], Keys(rig.Pane));
    });

    [Fact]
    public void Table_columns_follow_the_widest_rate_seen() => Sta.Run(() =>
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); // this test's own thread
        var busy = new Adapter(wifi: false);
        busy.Sample(received: FortyKbps);
        var idle = new Adapter(wifi: true);
        using var rig = new Rig(busy);
        // The capture widths the columns below are built from.
        Assert.Equal(86, PaneText.Width("40.0 Kbps", rig.Fonts.Value));
        Assert.Equal(60, PaneText.Width("0 Kbps", rig.Fonts.Value));

        using (Paint(rig.Pane, new Rectangle(0, 0, 1, 1))) { }
        Assert.Equal(86, rig.Pane.WidestRate);

        rig.Pane.Device = idle.Device;
        var info = rig.Pane.BuildInfo();

        Assert.Equal(["0 Kbps", "0 Kbps"], info.Items.Select(i => i.Item.Value));
        Assert.Equal([86, 86], info.Items.Select(i => i.Width));
        Assert.All(info.Table, r => Assert.Equal((125, 230), (rig.Pane.ContentLeft + r.KeyX, rig.Pane.ContentLeft + r.ValueX)));
        Assert.Equal(86, rig.Pane.WidestRate); // it never narrows

        // A pane that never drew the wider value sizes the column to the 60-px one.
        using var fresh = new Rig(idle);
        Assert.All(fresh.Pane.BuildInfo().Table, r => Assert.Equal(19 + 8 + 60 + 12, fresh.Pane.ContentLeft + r.KeyX));
    });

    [Fact]
    public void Signal_glyph_fills_one_bar_per_20_percent() => Sta.Run(() =>
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); // this test's own thread
        // Receiving 40.0 Kbps puts the value column at 230, as in the widest-rate test, so the glyph is at (228, 536).
        var wifi = new Adapter(wifi: true);
        wifi.Sample(received: FortyKbps);
        using var rig = new Rig(wifi);
        var glyph = new Rectangle(228, 536, 16, 16);

        using (var bmp = Paint(rig.Pane, glyph))
        {
            Assert.Equal(0xDBBAA0, Rgb(bmp, 10, 9)); // inside bar 4
            Assert.Equal(0xFFFFFF, Rgb(bmp, 13, 9)); // inside bar 5
            Assert.Equal(0xBB814E, Rgb(bmp, 9, 9)); // bar 4's left outline, bar 3's right
            Assert.Equal(0xBB814E, Rgb(bmp, 12, 0)); // bar 5's top left corner
            Assert.Equal(0xBB814E, Rgb(bmp, 15, 13)); // bar 5's bottom right corner
            Assert.Equal(0xFFFFFF, Rgb(bmp, 1, 9)); // above bar 1, whose top is at 10
            Assert.Equal(0xBB814E, Rgb(bmp, 0, 10));
            Assert.Equal(0xFFFFFF, Rgb(bmp, 7, 14)); // the row between the bars and the baseline
            Assert.Equal(0xBB814E, Rgb(bmp, 0, 15)); // the baseline
            Assert.Equal(0xBB814E, Rgb(bmp, 15, 15));
        }

        foreach (var (quality, bars) in new[] { (0, 0), (1, 1), (19, 1), (20, 2), (60, 4), (79, 4), (80, 5), (100, 5) })
        {
            wifi.Source.Wifi = wifi.Source.Wifi! with { SignalQuality = quality };
            wifi.Sample(received: FortyKbps);
            using var bmp = Paint(rig.Pane, glyph);

            // Every bar's inside reaches y 12; bar n's starts at x 3n + 1.
            for (var bar = 0; bar < 5; bar++)
                Assert.Equal(bar < bars ? 0xDBBAA0 : 0xFFFFFF, Rgb(bmp, 3 * bar + 1, 12));
        }
    });

    [Fact]
    public void Idle_scale_is_100_Kbps() => Sta.Run(() =>
    {
        using var idle = new Rig(new Adapter(wifi: false));
        Assert.Equal(("100 Kbps", 1e5f), (idle.Pane.ThroughputScale.Caption, idle.Pane.ThroughputScale.Max));

        // 400 Kbps either way takes the 500 Kbps step: the scale follows both series.
        var receiving = new Adapter(wifi: false);
        receiving.Sample(received: 50_000);
        var sending = new Adapter(wifi: false);
        sending.Sample(sent: 50_000);
        using var busy = new Rig(receiving);
        Assert.Equal(("500 Kbps", 5e5f), (busy.Pane.ThroughputScale.Caption, busy.Pane.ThroughputScale.Max));
        busy.Pane.Device = sending.Device;
        Assert.Equal(("500 Kbps", 5e5f), (busy.Pane.ThroughputScale.Caption, busy.Pane.ThroughputScale.Max));

        // The caption ends at the graph's right edge, 16 px above it, and the two scales' captions differ.
        var graph = idle.Pane.GraphBounds;
        var box = new Rectangle(graph.Right - 60, graph.Y - 16, 60, 13);
        using var idleBmp = Paint(idle.Pane, box);
        using var busyBmp = Paint(busy.Pane, box);
        var inked = false;
        var differs = false;
        for (var y = 0; y < box.Height; y++)
            for (var x = 0; x < box.Width; x++)
            {
                inked |= Rgb(idleBmp, x, y) != 0xFFFFFF;
                differs |= Rgb(idleBmp, x, y) != Rgb(busyBmp, x, y);
            }

        Assert.True(inked);
        Assert.True(differs);

        busy.Pane.Device = null;
        Assert.Equal(default, busy.Pane.ThroughputScale);
    });

    [Fact]
    public void Without_a_network_device_nothing_paints() => Sta.Run(() =>
    {
        using var rig = new Rig(new Adapter(wifi: true));

        foreach (var device in new Device?[] { null, new CpuDevice(new FakeCpuSource(new CpuInfo("c", 1, 1, 1, 1, "", 0, 0, 0))) })
        {
            rig.Pane.Device = device;
            using (var bmp = Paint(rig.Pane, new Rectangle(0, 400, 1052, 175)))
                Assert.All(Enumerable.Range(0, 1052 * 175), i => Assert.Equal(0xFFFFFF, Rgb(bmp, i % 1052, i / 1052)));
            Assert.Equal(Rectangle.Empty, rig.Pane.GraphBounds);
            Assert.Equal(0, rig.Pane.BuildInfo().Height);
            Assert.Equal(default, rig.Pane.ThroughputScale);
        }
    });

    [Fact]
    public void Painting_creates_no_gdi_or_user_objects() => Sta.Run(() =>
    {
        var wifi = new Adapter(wifi: true, dns: "home.arpa");
        for (var i = 0; i < 61; i++) wifi.Sample(received: (ulong)(1000 + 500 * i), sent: 3000);
        using var rig = new Rig(wifi);
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
