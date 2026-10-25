using System.Buffers.Binary;
using Tmcs.Devices;
using Tmcs.Devices.Gpu;
using Xunit.Abstractions;

namespace Tmcs.Tests.Devices;

public sealed class GpuDeviceTests(ITestOutputHelper output)
{
    const long Second = 10_000_000;

    static GpuInfo Info(int index = 0, long luid = 0x1D8C7) => new(
        index, luid, "NVIDIA GeForce RTX 3090",
        [GpuEngine.ThreeD, GpuEngine.Other, GpuEngine.VideoDecode, GpuEngine.Copy, GpuEngine.Copy, GpuEngine.Other, GpuEngine.VideoEncode],
        25_769_803_776, 34_172_811_264, 266_338_304, "32.0.16.1088", "7/22/2026", "12 (FL 12.1)", "PCI bus 1, device 0, function 0");

    [Fact]
    public void Accept_appends_all_seven_series()
    {
        var gpu = new GpuDevice(Info());
        var counters = new GpuCounters(14, 10, 1.5f, 2, 12, 3_119_497_216, 219_668_480, 37.8f);
        gpu.Accept(counters, Second);

        Assert.Equal(counters, gpu.Counters);
        Assert.Equal(1, gpu.History.Count);
        Assert.Equal(Second, gpu.History.LatestTime);
        float[] expected = [14, 10, 1.5f, 2, 12, 3_119_497_216, 219_668_480];
        for (var s = 0; s < expected.Length; s++) Assert.Equal(expected[s], gpu.History.ValueAt(s, 0));

        // The model hands the GPU its counters, so Sample reads nothing and appends nothing.
        gpu.Sample(2 * Second, []);
        Assert.Equal(1, gpu.History.Count);
    }

    [Fact]
    public void Title_pane_title_and_key()
    {
        var gpu = new GpuDevice(Info());
        Assert.Equal("GPU 0", gpu.Title);
        Assert.Equal("GPU", gpu.PaneTitle);
        Assert.Equal("NVIDIA GeForce RTX 3090", gpu.Subtitle);
        Assert.Equal("gpu:1d8c7", gpu.Key);
        Assert.Equal(DeviceKind.Gpu, gpu.Kind);

        var second = new GpuDevice(Info(index: 1, luid: 0x1_0000ABCD));
        Assert.Equal("GPU 1", second.Title);
        Assert.Equal("gpu:10000abcd", second.Key);
    }

    [Fact]
    public void Temperature_text_is_rounded_and_optional()
    {
        var gpu = new GpuDevice(Info());
        gpu.Accept(new GpuCounters(0, 0, 0, 0, 0, 0, 0, 37.8f), Second);
        Assert.Equal("(38 °C)", gpu.TemperatureText);

        gpu.Accept(new GpuCounters(0, 0, 0, 0, 0, 0, 0, null), 2 * Second);
        Assert.Null(gpu.TemperatureText);
    }

    [Fact]
    public void Corrupt_adapter_counts_read_as_none()
    {
        Span<byte> information = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(information, 3);
        BinaryPrimitives.WriteUInt32LittleEndian(information[4..], 0x8000_0005);
        Assert.Equal((3, 0), SystemGpuSource.AdapterCounts(information));

        BinaryPrimitives.WriteUInt32LittleEndian(information, uint.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(information[4..], 9);
        Assert.Equal((0, 9), SystemGpuSource.AdapterCounts(information));
    }

    [Fact]
    public void Real_listing_has_only_render_hardware_adapters()
    {
        using var source = new SystemGpuSource();
        var gpus = source.Enumerate();
        foreach (var g in gpus)
            output.WriteLine($"{g.Index} {g.Luid:x} '{g.Name}' nodes [{string.Join(",", g.NodeEngines)}] dedicated {g.DedicatedTotal} " +
                $"shared {g.SharedTotal} reserved {g.HardwareReserved} driver '{g.DriverVersion}' '{g.DriverDate}' dx '{g.DirectX}' '{g.Location}'");

        Assert.Equal(Enumerable.Range(0, gpus.Count), gpus.Select(g => g.Index));
        Assert.All(gpus, g =>
        {
            Assert.DoesNotContain("Basic Render", g.Name);
            Assert.DoesNotContain("DisplayLink", g.Name);
            Assert.DoesNotContain("Parsec", g.Name);
            Assert.True(g.DedicatedTotal > 0, $"{g.Name}: DedicatedTotal {g.DedicatedTotal}");
        });

        var counters = new GpuCounters[gpus.Count];
        Assert.True(source.ReadAll(counters));
        var hadTemperature = counters.Select(c => c.TemperatureC.HasValue).ToArray();

        // A rescan lists the same GPUs under the same LUIDs, so their keys and histories survive it. It also swaps the
        // adapter handles: a stale one would only lose the temperature, so that is what shows the swap went right.
        Assert.Equal(gpus.Select(g => g.Luid), source.Enumerate().Select(g => g.Luid));
        Assert.True(source.ReadAll(counters));
        Assert.Equal(hadTemperature, counters.Select(c => c.TemperatureC.HasValue));
    }

    [Fact]
    public void Real_read_all_does_not_allocate()
    {
        using var source = new SystemGpuSource();
        var counters = new GpuCounters[source.Enumerate().Count];
        // The warm-up sizes the PDH buffer and gives the instance maps room for twice the live instances, so GPU
        // processes that start during the loop don't make them grow.
        for (var i = 0; i < 3; i++) Assert.True(source.ReadAll(counters));

        var allRead = true;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) allRead &= source.ReadAll(counters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allRead);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Real_read_all_returns_sane_values()
    {
        using var source = new SystemGpuSource();
        var gpus = source.Enumerate();
        if (gpus.Count == 0)
        {
            output.WriteLine("No GPU is listed on this machine; nothing to read.");
            return;
        }

        var counters = new GpuCounters[gpus.Count];
        Assert.True(source.ReadAll(counters));
        Thread.Sleep(1000);
        Assert.True(source.ReadAll(counters));

        for (var i = 0; i < counters.Length; i++)
        {
            var c = counters[i];
            output.WriteLine($"GPU {i}: util {c.Utilization} 3D {c.ThreeD} copy {c.Copy} enc {c.VideoEncode} dec {c.VideoDecode} " +
                $"dedicated {c.DedicatedUsed} shared {c.SharedUsed} temp {c.TemperatureC}; tracked instances {source.TrackedInstances}");
            foreach (var percent in (float[])[c.Utilization, c.ThreeD, c.Copy, c.VideoEncode, c.VideoDecode])
                Assert.InRange(percent, 0, 100);
            Assert.True(c.Utilization >= Math.Max(Math.Max(c.ThreeD, c.Copy), Math.Max(c.VideoEncode, c.VideoDecode)));
            Assert.True(c.DedicatedUsed > 0, $"GPU {i}: DedicatedUsed {c.DedicatedUsed}");
            Assert.True(c.SharedUsed >= 0);
        }
        // Every live GPU has engine instances (dwm's at least), so none parsing would mean a broken name or layout.
        Assert.True(source.TrackedInstances > 0);
    }
}
