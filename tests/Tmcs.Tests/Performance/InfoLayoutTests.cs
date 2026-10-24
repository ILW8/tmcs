using Tmcs.Performance.Views;

namespace Tmcs.Tests.Performance;

// The widths are the captured ones, looked up by text, so these test the layout rules and not the fonts.
public class InfoLayoutTests
{
    static Func<string, int> Widths(params (string Text, int Width)[] widths)
    {
        var map = widths.ToDictionary(w => w.Text, w => w.Width);
        return text => map[text];
    }

    static IReadOnlyList<IReadOnlyList<InfoItem>> Lines(params InfoItem[][] lines) => lines;

    [Fact]
    public void Cpu_rows_flow_like_task_managers()
    {
        var labels = Widths(("Utilization", 53), ("Speed", 31), ("Processes", 51), ("Threads", 40), ("Handles", 44), ("Up time", 42),
            ("Base speed:", 62), ("Sockets:", 44), ("Cores:", 34), ("Logical processors:", 100), ("Virtualization:", 73),
            ("L1 cache:", 50), ("L2 cache:", 50), ("L3 cache:", 50));
        var values = Widths(("14%", 38), ("4.43 GHz", 79), ("481", 33), ("6789", 55), ("283149", 66), ("25:17:28:41", 100));
        StaticRow[] table =
        [
            new("Base speed:", "4.50 GHz"), new("Sockets:", "1"), new("Cores:", "16"), new("Logical processors:", "32"),
            new("Virtualization:", "Enabled"), new("L1 cache:", "1.0 MB"), new("L2 cache:", "16.0 MB"), new("L3 cache:", "64.0 MB"),
        ];

        var layout = InfoLayout.Rows(
            Lines([new("Utilization", "14%"), new("Speed", "4.43 GHz")],
                [new("Processes", "481"), new("Threads", "6789"), new("Handles", "283149")],
                [new("Up time", "25:17:28:41")]),
            table, labels, values);

        Assert.Equal([0, 73, 0, 71, 146, 0], layout.Items.Select(i => i.X));
        Assert.Equal([0, 0, 51, 51, 51, 102], layout.Items.Select(i => i.Y));
        Assert.Equal([0, 73, 0, 71, 146, 0], layout.Items.Select(i => i.TextX)); // no markers
        Assert.Equal([53, 79, 51, 55, 66, 100], layout.Items.Select(i => i.Width));
        Assert.All(layout.Table, r => Assert.Equal((224, 338), (r.KeyX, r.ValueX)));
        Assert.Equal([0, 18, 36, 54, 72, 90, 108, 126], layout.Table.Select(r => r.Y));
        Assert.Equal(table, layout.Table.Select(r => r.Row));
        Assert.Equal(145, layout.Height);
    }

    [Fact]
    public void Disk_marked_items_indent_their_text()
    {
        var labels = Widths(("Active time", 60), ("Average response time", 120), ("Read speed", 60), ("Write speed", 64),
            ("Capacity:", 50), ("Formatted:", 57), ("System disk:", 65), ("Page file:", 52), ("Type:", 30));
        var values = Widths(("0%", 30), ("0.4 ms", 60), ("0 KB/s", 55), ("1.2 MB/s", 76));
        StaticRow[] table = [new("Capacity:", "1.8 TB"), new("Formatted:", "1.8 TB"), new("System disk:", "Yes"), new("Page file:", "Yes"), new("Type:", "SSD")];

        var layout = InfoLayout.Rows(
            Lines([new("Active time", "0%"), new("Average response time", "0.4 ms")],
                [new("Read speed", "0 KB/s", InfoMarker.Solid), new("Write speed", "1.2 MB/s", InfoMarker.Dashed)]),
            table, labels, values);

        Assert.Equal([0, 80, 0, 88], layout.Items.Select(i => i.X));
        Assert.Equal([0, 80, 8, 96], layout.Items.Select(i => i.TextX));
        Assert.Equal([60, 120, 60, 76], layout.Items.Select(i => i.Width));
        Assert.Equal([0, 0, 51, 51], layout.Items.Select(i => i.Y));
        Assert.Equal([InfoMarker.None, InfoMarker.None, InfoMarker.Solid, InfoMarker.Dashed], layout.Items.Select(i => i.Item.Marker));
        Assert.All(layout.Table, r => Assert.Equal((212, 291), (r.KeyX, r.ValueX)));
        Assert.Equal(94, layout.Height);
    }

    [Fact]
    public void Wifi_signal_row_adds_a_pixel()
    {
        var labels = Widths(("Send", 26), ("Receive", 40), ("Adapter name:", 75), ("SSID:", 30), ("Connection type:", 91),
            ("IPv4 address:", 73), ("IPv6 address:", 73), ("Signal strength:", 85));
        var values = Widths(("0 Kbps", 60));

        var layout = InfoLayout.Rows(
            Lines([new("Send", "0 Kbps", InfoMarker.Dashed)], [new("Receive", "0 Kbps", InfoMarker.Solid)]),
            [
                new("Adapter name:", "Wi-Fi"), new("SSID:", "home"), new("Connection type:", "802.11ac"),
                new("IPv4 address:", "10.0.0.2"), new("IPv6 address:", "fe80::1"), new("Signal strength:", "", 19),
            ],
            labels, values);

        Assert.Equal([0, 18, 36, 54, 72, 90], layout.Table.Select(r => r.Y));
        Assert.Equal(109, layout.Height);
    }

    [Fact]
    public void Gpu_columns_share_their_widest_width()
    {
        var labels = Widths(("Utilization", 53), ("GPU Memory", 62), ("Dedicated GPU memory", 127), ("Shared GPU memory", 107),
            ("GPU Temperature", 95), ("Driver version:", 80), ("Driver date:", 63), ("DirectX version:", 86),
            ("Physical location:", 92), ("Hardware reserved memory:", 149));
        var values = Widths(("14%", 38), ("1.4/55.8 GB", 101), ("1.2/24.0 GB", 101), ("0.2/31.8 GB", 101), ("39 °C", 55));

        var layout = InfoLayout.Columns(
            Lines([new("Utilization", "14%"), new("GPU Memory", "1.4/55.8 GB")],
                [new("Dedicated GPU memory", "1.2/24.0 GB"), new("Shared GPU memory", "0.2/31.8 GB"), new("GPU Temperature", "39 °C")]),
            [
                new("Driver version:", "31.0.15.3623"), new("Driver date:", "6/2/2023"), new("DirectX version:", "12 (FL 12.1)"),
                new("Physical location:", "PCI bus 1, device 0, function 0"), new("Hardware reserved memory:", "209 MB"),
            ],
            labels, values);

        Assert.Equal([0, 0, 121, 121, 121], layout.Items.Select(i => i.X));
        Assert.Equal([0, 0, 121, 121, 121], layout.Items.Select(i => i.TextX));
        Assert.Equal([0, 51, 0, 51, 102], layout.Items.Select(i => i.Y));
        Assert.Equal([101, 101, 127, 127, 127], layout.Items.Select(i => i.Width));
        Assert.All(layout.Table, r => Assert.Equal((260, 260 + 149 + 14), (r.KeyX, r.ValueX)));
        Assert.Equal(145, layout.Height);
    }

    [Fact]
    public void Network_width_only_grows()
    {
        var labels = Widths(("Send", 26), ("Receive", 40), ("Adapter name:", 75), ("Connection type:", 91));
        var values = Widths(("0 Kbps", 60));

        var layout = InfoLayout.Rows(
            Lines([new("Send", "0 Kbps", InfoMarker.Dashed)], [new("Receive", "0 Kbps", InfoMarker.Solid)]),
            [new("Adapter name:", "Ethernet"), new("Connection type:", "Ethernet")],
            labels, values, minMarkedWidth: 86);

        Assert.Equal([86, 86], layout.Items.Select(i => i.Width));
        Assert.Equal([8, 8], layout.Items.Select(i => i.TextX));
        Assert.All(layout.Table, r => Assert.Equal(106, r.KeyX));
    }

    [Fact]
    public void Min_marked_width_leaves_unmarked_items_alone()
    {
        var layout = InfoLayout.Rows(Lines([new InfoItem("Utilization", "14%")]), [], Widths(("Utilization", 53)), Widths(("14%", 38)), minMarkedWidth: 86);

        Assert.Equal(53, layout.Items.Single().Width);
        Assert.Equal(43, layout.Height); // one row: 51 − 8
    }
}
