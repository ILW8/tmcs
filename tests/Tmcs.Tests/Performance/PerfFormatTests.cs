using System.Globalization;
using Tmcs.Performance;

namespace Tmcs.Tests.Performance;

public class PerfFormatTests : IDisposable
{
    const double KiB = 1024, MiB = KiB * 1024, GiB = MiB * 1024;

    readonly CultureInfo _saved = CultureInfo.CurrentCulture;

    public PerfFormatTests() => CultureInfo.CurrentCulture = new("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _saved;

    [Theory]
    [InlineData(1000204886016.0, "932 GB")]          // Disk capacity, 931.51 GiB
    [InlineData(931.496 * GiB, "932 GB")]            // Disk 2's formatted size: rounded to 931.5 first
    [InlineData(931.386 * GiB, "931 GB")]            // the formatted size of disks 0 and 1
    [InlineData(2000398934016.0, "1.8 TB")]
    [InlineData(356.5 * MiB, "357 MB")]              // hardware-reserved memory
    [InlineData(1 * MiB, "1.0 MB")]                  // the L1 cache
    [InlineData(64 * MiB, "64.0 MB")]
    [InlineData(512 * KiB, "512 KB")]
    public void Size_picks_the_largest_unit_and_the_precision_rule(double bytes, string expected) =>
        Assert.Equal(expected, PerfFormat.Size(bytes));

    [Fact]
    public void Number_shows_zero_bare_and_drops_decimals_from_100()
    {
        Assert.Equal("0", PerfFormat.Number(0)); Assert.Equal("1.6", PerfFormat.Number(1.6));
        Assert.Equal("100", PerfFormat.Number(99.96)); Assert.Equal("216", PerfFormat.Number(216.4));
    }

    [Fact]
    public void Pair_shares_the_larger_values_precision()
    {
        const double G = 1L << 30;
        Assert.Equal("55/120 GB", PerfFormat.Pair(54.7 * G, 119.7 * G, "GB", G));
        Assert.Equal("38.4/63.7 GB", PerfFormat.Pair(38.4 * G, 63.7 * G, "GB", G));
    }

    [Fact]
    public void Disk_rates()
    {
        Assert.Equal("0 KB/s", PerfFormat.DiskRate(0)); Assert.Equal("1.3 MB/s", PerfFormat.DiskRate(1.3 * 1048576));
        Assert.Equal("708 MB/s", PerfFormat.DiskRate(708 * 1048576.0));
    }

    [Fact]
    public void Bit_rates_are_whole_kilobytes_times_8()
    {
        Assert.Equal("40.0 Kbps", PerfFormat.BitRate(40_300)); Assert.Equal("0 Kbps", PerfFormat.BitRate(0));
        Assert.Equal("120 Kbps", PerfFormat.BitRate(119_000)); Assert.Equal("216 Mbps", PerfFormat.BitRate(216_000_000));
        Assert.Equal(16_000, PerfFormat.QuantizeBits(16_700));
    }

    [Fact]
    public void Sidebar_rates_share_one_unit()
    {
        Assert.Equal(("8.0", "40.0 Kbps"), PerfFormat.SidebarRates(8_000, 40_000));
        Assert.Equal(("0", "8.0 Kbps"), PerfFormat.SidebarRates(0, 8_000));
        Assert.Equal(("0.3", "21.7 Mbps"), PerfFormat.SidebarRates(300_000, 21_700_000));
        Assert.Equal(("1.4", "216 Mbps"), PerfFormat.SidebarRates(1_400_000, 216_000_000));
    }

    [Fact]
    public void Ten_gigabit_rates() // the owner's NC550SFP is 10GbE
    {
        Assert.Equal("9.4 Gbps", PerfFormat.BitRate(9_400_000_000));
        Assert.Equal(("0.1", "9.4 Gbps"), PerfFormat.SidebarRates(100_000_000, 9_400_000_000));
    }

    [Fact]
    public void Decimal_separator_follows_the_culture()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal("38,4/63,7 GB", PerfFormat.Pair(38.4 * GiB, 63.7 * GiB, "GB", GiB));
        Assert.Equal("4,42 GHz", PerfFormat.Ghz(4420));
    }

    [Fact]
    public void Cpu_and_misc_formats()
    {
        Assert.Equal("10%", PerfFormat.Percent(9.5)); Assert.Equal("6%", PerfFormat.Percent(6.24));
        Assert.Equal("4.42 GHz", PerfFormat.Ghz(4420)); Assert.Equal("4.50 GHz", PerfFormat.Ghz(4500));
        Assert.Equal("25:17:28:41", PerfFormat.UpTime(new TimeSpan(25, 17, 28, 41, 900)));
        Assert.Equal("0:00:00:05", PerfFormat.UpTime(TimeSpan.FromSeconds(5)));
        Assert.Equal("0 ms", PerfFormat.Milliseconds(0)); Assert.Equal("38 °C", PerfFormat.Celsius(37.8));
    }
}
