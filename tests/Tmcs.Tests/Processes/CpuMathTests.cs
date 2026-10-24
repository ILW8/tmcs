using Tmcs.Native;
using Tmcs.Processes;

namespace Tmcs.Tests.Processes;

public class CpuMathTests
{
    [Fact]
    public void Percent_is_cpu_over_wall_times_processors()
        => Assert.Equal(25.0, CpuMath.Percent(10_000_000, 10_000_000, 4), 6);

    [Fact]
    public void Percent_is_clamped_to_100()
        => Assert.Equal(100.0, CpuMath.Percent(90_000_000, 10_000_000, 4));

    [Theory]
    [InlineData(5_000_000, 0)]
    [InlineData(5_000_000, -10)]
    public void Percent_is_zero_when_wall_delta_is_not_positive(long cpu, long wall)
        => Assert.Equal(0, CpuMath.Percent(cpu, wall, 4));

    [Fact]
    public void Percent_is_zero_when_cpu_time_went_backwards()
        => Assert.Equal(0, CpuMath.Percent(-1_000, 10_000_000, 4));

    // Spec section 6 divides by the machine's logical processors. Environment.ProcessorCount follows this process's affinity, job
    // limits and DOTNET_PROCESSOR_COUNT, so it can be smaller.
    [Fact]
    public void Logical_processors_are_the_machine_s_across_all_groups()
    {
        var machine = Kernel32.GetActiveProcessorCount(Kernel32.AllProcessorGroups);

        Assert.True(machine >= 1);
        Assert.True(machine >= Environment.ProcessorCount, $"{machine} active processors, but Environment.ProcessorCount is {Environment.ProcessorCount}");
        Assert.Equal((int)machine, CpuMath.MachineLogicalProcessors());
    }
}
