using Tmcs.Devices.Disk;
using Tmcs.Devices.Network;
using Tmcs.Performance;
using Tmcs.Processes;

namespace Tmcs.Tests.Performance;

public sealed class PerformanceModelTests
{
    const long Second = 10_000_000;

    // Written out rather than built from the GUIDs, so a wrong key format shows up.
    const string KeyA = "net:{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}";
    const string KeyB = "net:{bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb}";
    const string KeyC = "net:{cccccccc-cccc-cccc-cccc-cccccccccccc}";

    static readonly IReadOnlyList<RawProcess> Snapshot = [];

    static string[] Keys(PerformanceModel model) => [.. model.Devices.Select(d => d.Key)];

    static RawProcess Process(int pid, int parent, string name) => new(pid, parent, 0, name, 0, 0, 0, 0, 1, 10, 0, 8, false, 1 << 20);

    [Fact]
    public void Devices_are_in_sidebar_order()
    {
        using var model = new PerformanceModel(FakeDeviceSources.Create());

        Assert.Equal(["cpu", "memory", "disk:0", "disk:4", KeyA, KeyB, "gpu:1d8c7"], Keys(model));
        Assert.Same(model.Cpu, model.Devices[0]);
        Assert.Same(model.Memory, model.Devices[1]);
    }

    [Fact]
    public void Sample_appends_one_point_to_every_device()
    {
        using var model = new PerformanceModel(FakeDeviceSources.Create());

        for (var i = 1; i <= 3; i++) model.Sample(i * Second, Snapshot);

        Assert.All(model.Devices, d => Assert.Equal(3, d.History.Count));
    }

    [Fact]
    public void A_failed_disk_drops_out_on_the_next_sample()
    {
        var sources = FakeDeviceSources.Create();
        using var model = new PerformanceModel(sources);
        var disk0 = model.Devices[2];
        var changes = 0;
        model.DevicesChanged += () => changes++;
        model.Sample(Second, Snapshot);

        sources.FakeDisks().Failing.Add(4);
        sources.FakeDisks().Disks = [FakeDeviceSources.Disk(0, "C:")];
        model.Sample(2 * Second, Snapshot);

        var disk4 = Assert.IsType<DiskDevice>(model.Devices.Single(d => d.Key == "disk:4"));
        Assert.True(disk4.Failed);
        Assert.Equal(0, changes);

        model.Sample(3 * Second, Snapshot);

        Assert.DoesNotContain(model.Devices, d => d.Key == "disk:4");
        Assert.Equal(1, changes);
        Assert.Same(disk0, model.Devices.Single(d => d.Key == "disk:0"));
        Assert.Equal(3, disk0.History.Count);
    }

    [Fact]
    public void Adapter_change_keeps_the_history_and_updates_the_info()
    {
        var sources = FakeDeviceSources.Create();
        using var model = new PerformanceModel(sources);
        model.Sample(Second, Snapshot);
        model.Sample(2 * Second, Snapshot);
        var b = model.Devices.Single(d => d.Key == KeyB);
        var changes = 0;
        model.DevicesChanged += () => changes++;

        sources.FakeNetwork().Adapters = [FakeDeviceSources.Adapter(FakeDeviceSources.B, 2, ipv4: "10.0.0.9"), FakeDeviceSources.Adapter(FakeDeviceSources.C, 3)];
        sources.FakeNetwork().Changed = true;
        model.Sample(3 * Second, Snapshot);

        Assert.Equal(["cpu", "memory", "disk:0", "disk:4", KeyB, KeyC, "gpu:1d8c7"], Keys(model));
        var kept = Assert.IsType<NetworkDevice>(model.Devices[4]);
        Assert.Same(b, kept);
        Assert.Equal("10.0.0.9", kept.Info.IPv4);
        Assert.Equal(3, kept.History.Count);
        Assert.Equal(1, model.Devices[5].History.Count);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Info_only_rescan_updates_the_info_and_raises_nothing()
    {
        var sources = FakeDeviceSources.Create();
        using var model = new PerformanceModel(sources);
        model.Sample(Second, Snapshot);
        model.Sample(2 * Second, Snapshot);
        var b = model.Devices.Single(d => d.Key == KeyB);
        var changes = 0;
        model.DevicesChanged += () => changes++;

        // A DHCP renewal: the same adapters in the same order, B with a new address.
        sources.FakeNetwork().Adapters = [FakeDeviceSources.Adapter(FakeDeviceSources.A, 1), FakeDeviceSources.Adapter(FakeDeviceSources.B, 2, ipv4: "10.0.0.9")];
        sources.FakeNetwork().Changed = true;
        model.Sample(3 * Second, Snapshot);

        Assert.Equal(2, sources.FakeNetwork().Enumerations);
        Assert.Equal(["cpu", "memory", "disk:0", "disk:4", KeyA, KeyB, "gpu:1d8c7"], Keys(model));
        var kept = Assert.IsType<NetworkDevice>(model.Devices[5]);
        Assert.Same(b, kept);
        Assert.Equal("10.0.0.9", kept.Info.IPv4);
        Assert.Equal(3, kept.History.Count);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Marked_changes_rescan_on_the_next_sample_only()
    {
        var sources = FakeDeviceSources.Create();
        using var model = new PerformanceModel(sources);
        var (disks, network, gpu) = (sources.FakeDisks(), sources.FakeNetwork(), sources.FakeGpu());
        model.Sample(Second, Snapshot);
        var changes = 0;
        model.DevicesChanged += () => changes++;
        Assert.Equal((1, 1, 1), (disks.Enumerations, network.Enumerations, gpu.Enumerations));

        model.MarkDevicesChanged();
        Assert.Equal((1, 1, 1), (disks.Enumerations, network.Enumerations, gpu.Enumerations));

        model.Sample(2 * Second, Snapshot);
        Assert.Equal((2, 1, 2), (disks.Enumerations, network.Enumerations, gpu.Enumerations));

        model.Sample(3 * Second, Snapshot);
        Assert.Equal((2, 1, 2), (disks.Enumerations, network.Enumerations, gpu.Enumerations));

        // The same devices came back: nothing for the sidebar to rebuild, and every history goes on.
        Assert.Equal(0, changes);
        Assert.All(model.Devices, d => Assert.Equal(3, d.History.Count));
    }

    [Fact]
    public void Gpu_luid_change_replaces_the_gpu()
    {
        var sources = FakeDeviceSources.Create();
        using var model = new PerformanceModel(sources);
        var gpu = sources.FakeGpu();
        model.Sample(Second, Snapshot);
        var changes = 0;
        model.DevicesChanged += () => changes++;

        // A driver restart: the old LUID's reads fail, and the listing has the GPU under a new one.
        gpu.Succeeds = false;
        gpu.Gpus = [FakeDeviceSources.Gpu(0x2000)];
        model.Sample(2 * Second, Snapshot);
        Assert.Equal("gpu:1d8c7", model.Devices[^1].Key);
        Assert.Equal(1, model.Devices[^1].History.Count);

        gpu.Succeeds = true;
        model.Sample(3 * Second, Snapshot);

        Assert.Equal("gpu:2000", model.Devices[^1].Key);
        Assert.Equal(1, model.Devices[^1].History.Count);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Failing_sources_never_throw()
    {
        var sources = FakeDeviceSources.Create();
        sources.FakeGpu().Gpus = [];
        sources.FakeCpu().Succeeds = false;
        sources.FakeMemory().Succeeds = false;
        sources.FakeDisks().Succeeds = false;
        sources.FakeNetwork().Succeeds = false;
        sources.FakeGpu().Succeeds = false;
        using var model = new PerformanceModel(sources);

        for (var i = 1; i <= 100; i++) model.Sample(i * Second, Snapshot);

        Assert.Equal(["cpu", "memory", "disk:0", "disk:4", KeyA, KeyB], Keys(model));
        Assert.All(model.Devices, d => Assert.Equal(0, d.History.Count));
        // With no GPU listed a failed read has nothing to rescan, so the GPUs are listed only by the constructor.
        Assert.Equal(1, sources.FakeGpu().Enumerations);
    }

    [Fact]
    public void Steady_sampling_does_not_allocate()
    {
        // A real snapshot is a non-empty List, whose interface enumerator would box where an empty array's is cached. Memory
        // Compression comes last, so the memory device walks the whole list.
        List<RawProcess> snapshot =
        [
            Process(0, 0, "System Idle Process"),
            Process(4, 0, "System"),
            Process(5000, 1000, "app.exe"),
            Process(3000, 4, "Memory Compression"),
        ];
        using var model = new PerformanceModel(FakeDeviceSources.Create());
        var time = 0L;
        for (var i = 0; i < 3; i++) model.Sample(time += Second, snapshot);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) model.Sample(time += Second, snapshot);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
