namespace Tmcs.Processes;

internal static class CpuMath
{
    /// <summary>CPU use as a percentage of the whole machine (all logical processors), clamped to [0, 100].</summary>
    /// <param name="deltaCpu100ns">Kernel + user time consumed since the previous sample.</param>
    /// <param name="deltaWall100ns">Wall-clock time since the previous sample.</param>
    /// <param name="logicalProcessors">Number of logical processors the machine has.</param>
    public static double Percent(long deltaCpu100ns, long deltaWall100ns, int logicalProcessors)
    {
        if (deltaWall100ns <= 0 || logicalProcessors <= 0)
            return 0;
        return Math.Clamp(100.0 * deltaCpu100ns / ((double)deltaWall100ns * logicalProcessors), 0, 100);
    }
}
