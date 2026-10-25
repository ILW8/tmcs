using Tmcs.Devices;
using Tmcs.Devices.Cpu;
using Tmcs.Processes;

namespace Tmcs.Tests.Devices;

public class CpuDeviceTests
{
    static RawProcess Proc(int threads, int handles) => new(1, 0, 0, "p.exe", 0, 0, 0, 0, threads, handles, 1, 8, false);

    [Fact]
    public void Sample_appends_total_and_per_processor_series()
    {
        var fake = new FakeCpuSource
        {
            Loads = [new(10, 1), new(20, 2), new(30, 3), new(40, 4)],
            Total = 25, Performance = 98,
        };
        var cpu = new CpuDevice(fake);
        cpu.Sample(10_000_000, []);

        Assert.Equal(1, cpu.History.Count);
        Assert.Equal(25, cpu.History.ValueAt(0, 0));
        Assert.Equal(40, cpu.History.ValueAt(1 + 2 * 3, 0));
        Assert.Equal(4, cpu.History.ValueAt(2 + 2 * 3, 0));
        Assert.Equal(25, cpu.Utilization);
        Assert.Equal(4410, cpu.SpeedMhz, 6);
    }

    [Fact]
    public void Counts_come_from_the_snapshot()
    {
        var cpu = new CpuDevice(new FakeCpuSource());
        cpu.Sample(10_000_000, [Proc(2, 10), Proc(3, 20), Proc(4, 30)]);
        Assert.Equal(3, cpu.Processes);
        Assert.Equal(9, cpu.Threads);
        Assert.Equal(60, cpu.Handles);
    }

    [Fact]
    public void Failed_read_appends_nothing_and_keeps_latest_values()
    {
        var fake = new FakeCpuSource { Total = 25, Performance = 98 };
        var cpu = new CpuDevice(fake);
        cpu.Sample(10_000_000, [Proc(2, 10)]);

        fake.Succeeds = false;
        fake.Total = 90;
        cpu.Sample(20_000_000, [Proc(2, 10), Proc(3, 20)]);

        Assert.Equal(1, cpu.History.Count);
        Assert.Equal(25, cpu.Utilization);
        Assert.Equal(4410, cpu.SpeedMhz, 6);
    }

    [Fact]
    public void Failed_read_still_takes_the_counts_from_the_snapshot()
    {
        var fake = new FakeCpuSource();
        var cpu = new CpuDevice(fake);
        cpu.Sample(10_000_000, [Proc(2, 10)]);

        fake.Succeeds = false;
        fake.Up = TimeSpan.FromHours(3);
        cpu.Sample(20_000_000, [Proc(2, 10), Proc(3, 20)]);

        Assert.Equal(2, cpu.Processes);
        Assert.Equal(5, cpu.Threads);
        Assert.Equal(30, cpu.Handles);
        Assert.Equal(TimeSpan.FromHours(3), cpu.UpTime);
    }

    [Fact]
    public void Title_and_subtitle()
    {
        var cpu = new CpuDevice(new FakeCpuSource());
        Assert.Equal("CPU", cpu.Title);
        Assert.Equal("CPU", cpu.PaneTitle);
        Assert.Equal(cpu.Info.Name, cpu.Subtitle);
        Assert.Equal("cpu", cpu.Key);
        Assert.Equal(DeviceKind.Cpu, cpu.Kind);
    }

    [Fact]
    public void Real_source_reads_every_logical_processor()
    {
        using var src = new PdhCpuSource();
        Assert.Equal(CpuMath.MachineLogicalProcessors(), src.Info.LogicalProcessors);
        var loads = new CpuLoad[src.Info.LogicalProcessors];
        src.Read(loads, out _, out _);
        Thread.Sleep(1100);

        Assert.True(src.Read(loads, out var total, out var perf));
        foreach (var l in loads)
        {
            Assert.InRange(l.Utility, 0, 100);
            Assert.InRange(l.Privileged, 0, 100);
            Assert.True(l.Privileged <= l.Utility + 1, $"privileged {l.Privileged} > utility {l.Utility}");
        }
        Assert.InRange(total, 0, 100);
        Assert.True(perf > 0);
    }

    [Fact]
    public void Real_static_info_is_sane()
    {
        using var src = new PdhCpuSource();
        var i = src.Info;
        Assert.True(i.Sockets >= 1);
        Assert.True(i.Cores >= i.Sockets);
        Assert.True(i.LogicalProcessors >= i.Cores);
        Assert.True(i.L1Bytes > 0);
        Assert.True(i.BaseMhz > 0);
        Assert.False(string.IsNullOrWhiteSpace(i.Name));
        Assert.True(src.UpTime() > TimeSpan.Zero);
    }

    [Fact]
    public void Real_read_does_not_allocate()
    {
        using var src = new PdhCpuSource();
        var loads = new CpuLoad[src.Info.LogicalProcessors];
        src.Read(loads, out _, out _);
        src.Read(loads, out _, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) src.Read(loads, out _, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
