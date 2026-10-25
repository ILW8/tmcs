using Tmcs.Performance;

namespace Tmcs.Tests.Performance;

public sealed class GraphScalesTests
{
    const float MiB = 1 << 20;

    [Fact]
    public void Network_steps()
    {
        Assert.Equal((100_000f, "100 Kbps"), GraphScales.Network(0));
        Assert.Equal("100 Kbps", GraphScales.Network(40_000).Caption);
        Assert.Equal((500_000f, "500 Kbps"), GraphScales.Network(450_000));
        Assert.Equal("1 Gbps", GraphScales.Network(323_000_000).Caption);
        Assert.Equal((1e10f, "10 Gbps"), GraphScales.Network(9_400_000_000));
    }

    [Fact]
    public void Disk_steps()
    {
        Assert.Equal("100 KB/s", GraphScales.Disk(0).Caption);
        Assert.Equal("10 MB/s", GraphScales.Disk(1.3f * MiB).Caption);
        Assert.Equal("1 GB/s", GraphScales.Disk(708 * MiB).Caption);
    }

    [Fact]
    public void A_step_holds_its_own_value_and_the_top_step_holds_anything_larger()
    {
        Assert.Equal((100_000f, "100 Kbps"), GraphScales.Network(100_000));
        Assert.Equal("1 Mbps", GraphScales.Network(500_001).Caption);
        Assert.Equal("100 Gbps", GraphScales.Network(1e12f).Caption);
        Assert.Equal((102_400f, "100 KB/s"), GraphScales.Disk(102_400));
        Assert.Equal((250 * MiB, "250 MB/s"), GraphScales.Disk(101 * MiB));
        Assert.Equal("100 GB/s", GraphScales.Disk(1e12f).Caption);
        Assert.Equal("100 Kbps", GraphScales.Network(float.NaN).Caption); // NaN counts as 0, as the painter draws it
    }

    [Fact]
    public void Neither_method_allocates()
    {
        GraphScales.Network(1);
        GraphScales.Disk(1);
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 1000; i++)
        {
            GraphScales.Network(i * 1e7f);
            GraphScales.Disk(i * 1e6f);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
