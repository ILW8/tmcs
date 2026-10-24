using Tmcs.Devices.Gpu;

namespace Tmcs.Tests.Devices;

public sealed class GpuEngineCountersTests
{
    const long Luid = 0x1D8C7;
    const long Second = 10_000_000;

    static string Name(int pid, int eng, string high = "00000000", string low = "0001D8C7") =>
        $"pid_{pid}_luid_0x{high}_0x{low}_phys_0_eng_{eng}_engtype_3D";

    [Fact]
    public void Node_percent_is_running_time_delta_over_time_delta()
    {
        var c = new GpuEngineCounters();
        c.Accumulate(Name(10, 0), 1_000_000, 0);
        c.EndSample();
        c.Accumulate(Name(10, 0), 3_000_000, Second);
        c.EndSample();

        Assert.Equal(20, c.NodePercent(Luid, 0), 3);
        Assert.Equal(0, c.NodePercent(Luid, 1));
    }

    [Fact]
    public void Instances_are_summed_per_node()
    {
        var c = new GpuEngineCounters();
        c.Accumulate(Name(10, 0), 5_000_000, Second);
        c.Accumulate(Name(11, 0), 7_000_000, Second);
        c.EndSample();
        c.Accumulate(Name(10, 0), 6_000_000, 2 * Second);
        c.Accumulate(Name(11, 0), 8_000_000, 2 * Second);
        c.EndSample();

        Assert.Equal(20, c.NodePercent(Luid, 0), 3);
    }

    [Fact]
    public void Other_luids_are_ignored()
    {
        var c = new GpuEngineCounters();
        c.Accumulate(Name(10, 0), 0, Second);
        c.Accumulate(Name(10, 0, low: "000207C4"), 0, Second);
        c.Accumulate(Name(10, 0, high: "00000001"), 0, Second);
        c.EndSample();
        c.Accumulate(Name(10, 0), 1_000_000, 2 * Second);
        c.Accumulate(Name(10, 0, low: "000207C4"), 5_000_000, 2 * Second);
        c.Accumulate(Name(10, 0, high: "00000001"), 3_000_000, 2 * Second);
        c.EndSample();

        Assert.Equal(10, c.NodePercent(Luid, 0), 3);
        Assert.Equal(50, c.NodePercent(0x207C4, 0), 3);
        Assert.Equal(30, c.NodePercent(0x1_0001D8C7, 0), 3);
    }

    [Fact]
    public void A_new_instance_counts_from_its_second_sample()
    {
        var c = new GpuEngineCounters();
        c.Accumulate(Name(10, 0), 0, Second);
        c.EndSample();

        c.Accumulate(Name(10, 0), 1_000_000, 2 * Second);
        c.Accumulate(Name(11, 0), 9_000_000, 2 * Second);
        c.EndSample();
        Assert.Equal(10, c.NodePercent(Luid, 0), 3);

        c.Accumulate(Name(10, 0), 2_000_000, 3 * Second);
        c.Accumulate(Name(11, 0), 11_000_000, 3 * Second);
        c.EndSample();
        Assert.Equal(30, c.NodePercent(Luid, 0), 3);
    }

    [Fact]
    public void Negative_deltas_and_unparsable_names_add_nothing()
    {
        var c = new GpuEngineCounters();
        c.Accumulate(Name(10, 0), 9_000_000, Second);
        c.Accumulate(Name(11, 2), 0, Second);
        c.EndSample();

        // A pid reused within a tick, or a counter reset by a driver restart, makes the running time go backwards.
        c.Accumulate(Name(10, 0), 1_000_000, 2 * Second);
        c.Accumulate(Name(11, 2), 30_000_000, 2 * Second);
        c.Accumulate("_Total", 5_000_000, 2 * Second);
        c.Accumulate("pid_12_luid_0x00000000_0x0001D8C7_phys_0", 5_000_000, 2 * Second);
        c.Accumulate("pid_x_luid_0x00000000_0x0001D8C7_phys_0_eng_0", 5_000_000, 2 * Second);
        c.EndSample();

        Assert.Equal(0, c.NodePercent(Luid, 0));
        Assert.Equal(100, c.NodePercent(Luid, 2));
        Assert.Equal(2, c.TrackedInstances);
    }

    [Fact]
    public void Exited_instances_are_forgotten()
    {
        var c = new GpuEngineCounters();
        for (var sample = 0; sample < 1000; sample++)
        {
            for (var i = 0; i < 500; i++) c.Accumulate(Name(sample * 500 + i, 0), sample, sample * Second);
            c.EndSample();
        }
        Assert.Equal(500, c.TrackedInstances);
    }

    [Fact]
    public void Parsing_does_not_allocate()
    {
        var names = new string[500];
        for (var i = 0; i < names.Length; i++) names[i] = Name(1000 + i, i % 13);

        var c = new GpuEngineCounters();
        for (var sample = 0; sample < 3; sample++)
        {
            foreach (var n in names) c.Accumulate(n, sample * 1000, sample * Second);
            c.EndSample();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var n in names) c.Accumulate(n, 5000, 4 * Second);
        c.EndSample();
        _ = c.NodePercent(Luid, 0);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Node_sums_are_sized_even_when_warm_up_samples_share_a_timestamp()
    {
        // PDH only moves an instance's timestamp when the counter updates, so back-to-back reads can repeat it and
        // yield no deltas. The per-node maps must still have room for the nodes before the first delta arrives.
        var names = Enumerable.Range(0, 40).Select(n => Name(10, n)).ToArray();
        var c = new GpuEngineCounters();
        for (var i = 0; i < 3; i++)
        {
            foreach (var n in names) c.Accumulate(n, 1000, Second);
            c.EndSample();
            _ = c.NodePercent(Luid, 0);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 2; i < 6; i++)
        {
            foreach (var n in names) c.Accumulate(n, 1000 * i, i * Second);
            c.EndSample();
            _ = c.NodePercent(Luid, 0);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
