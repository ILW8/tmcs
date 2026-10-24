using Tmcs.Devices.Network;

namespace Tmcs.Tests.Devices;

public sealed class NetworkDeviceTests
{
    const long Second = 10_000_000;
    static readonly Guid TestGuid = new("3F2504E0-4F89-11D3-9A0C-0305E82C3301");

    static AdapterInfo Info(bool wifi) => new(TestGuid, 7, wifi ? "Wi-Fi 2" : "Ethernet 4", "Test adapter", null, wifi, "10.0.0.2", null);

    static (NetworkDevice Net, FakeNetworkSource Fake) Make(bool wifi = false)
    {
        var fake = new FakeNetworkSource();
        return (new NetworkDevice(fake, Info(wifi)), fake);
    }

    static void Read(NetworkDevice net, FakeNetworkSource fake, long time, ulong inOctets, ulong outOctets, bool up = true)
    {
        fake.Counters = new AdapterCounters(inOctets, outOctets, up);
        net.Sample(time, []);
    }

    [Fact]
    public void Rates_are_bits_per_second_from_octet_deltas()
    {
        var (net, fake) = Make();
        Read(net, fake, Second, 1000, 2000);
        Read(net, fake, 2 * Second, 6000, 3000);

        Assert.Equal(40_000, net.ReceiveBitsPerSecond, 6);
        Assert.Equal(8_000, net.SendBitsPerSecond, 6);
        Assert.Equal(40_000f, net.History.ValueAt(0, 1), 3);
        Assert.Equal(8_000f, net.History.ValueAt(1, 1), 3);
    }

    [Fact]
    public void First_read_and_backwards_counters_give_zero()
    {
        var (net, fake) = Make();
        Read(net, fake, Second, 1000, 1000);
        Assert.Equal(0, net.ReceiveBitsPerSecond);
        Assert.Equal(0, net.SendBitsPerSecond);
        Assert.Equal(1, net.History.Count);

        Read(net, fake, 2 * Second, 500, 500);
        Assert.Equal(0, net.ReceiveBitsPerSecond);
        Assert.Equal(0, net.SendBitsPerSecond);

        // The backwards read became the new baseline.
        Read(net, fake, 3 * Second, 1500, 600);
        Assert.Equal(8_000, net.ReceiveBitsPerSecond, 6);
        Assert.Equal(800, net.SendBitsPerSecond, 6);
    }

    [Fact]
    public void Wifi_title_connection_type_and_key()
    {
        var (wifi, wifiFake) = Make(wifi: true);
        wifiFake.Wifi = new WifiInfo("frame", "802.11ax", 60);
        Read(wifi, wifiFake, Second, 0, 0);
        Assert.Equal("Wi-Fi", wifi.Title);
        Assert.Equal("802.11ax", wifi.ConnectionType);
        Assert.Equal("frame", wifi.Wifi?.Ssid);
        Assert.Equal("Test adapter", wifi.Subtitle);
        Assert.Equal("net:{3f2504e0-4f89-11d3-9a0c-0305e82c3301}", wifi.Key);

        var (wired, wiredFake) = Make();
        Read(wired, wiredFake, Second, 0, 0);
        Assert.Equal("Ethernet", wired.Title);
        Assert.Equal("Ethernet", wired.ConnectionType);
        Assert.Null(wired.Wifi);
    }

    [Fact]
    public void Wifi_without_data_has_a_blank_connection_type()
    {
        var (wifi, fake) = Make(wifi: true);
        fake.Wifi = null;
        Read(wifi, fake, Second, 0, 0);
        Assert.Equal("Wi-Fi", wifi.Title);
        Assert.Equal("", wifi.ConnectionType);
    }

    [Fact]
    public void Down_adapter_marks_the_device_failed()
    {
        var (net, fake) = Make();
        Read(net, fake, Second, 10, 10, up: false);
        Assert.True(net.Failed);
        Assert.Equal(0, net.History.Count);

        Read(net, fake, 2 * Second, 10, 10);
        Assert.False(net.Failed);
        Assert.Equal(1, net.History.Count);

        fake.Succeeds = false;
        net.Sample(3 * Second, []);
        Assert.True(net.Failed);
        Assert.Equal(1, net.History.Count);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(60, 4)]
    [InlineData(79, 4)]
    [InlineData(80, 5)]
    [InlineData(100, 5)]
    public void Signal_bars(int quality, int bars) => Assert.Equal(bars, NetworkDevice.SignalBars(quality));

    [Fact]
    public void Real_listing_matches_up_non_loopback_adapters()
    {
        using var source = new SystemNetworkSource();
        var adapters = source.Enumerate();
        Assert.NotEmpty(adapters);
        foreach (var a in adapters)
        {
            Assert.False(string.IsNullOrEmpty(a.Alias));
            Assert.False(string.IsNullOrEmpty(a.Description));
            Assert.NotEqual(0UL, a.Luid);
            Assert.DoesNotContain("Loopback", a.Description);
            Assert.True(source.Read(a.Luid, out var c));
            Assert.True(c.IsUp);
        }
    }

    [Fact]
    public void Real_change_flag_starts_set_and_enumerate_clears_it()
    {
        using var source = new SystemNetworkSource();
        Assert.True(source.Changed);
        source.Enumerate();
        Assert.False(source.Changed);
    }
}
