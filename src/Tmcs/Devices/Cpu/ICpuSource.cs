namespace Tmcs.Devices.Cpu;

/// <summary>One processor's load, both 0 to 100.</summary>
internal readonly record struct CpuLoad(float Utility, float Privileged);

internal sealed record CpuInfo(
    string Name,
    double BaseMhz,
    int Sockets,
    int Cores,
    int LogicalProcessors,
    string Virtualization,
    long L1Bytes,
    long L2Bytes,
    long L3Bytes);

internal interface ICpuSource : IDisposable
{
    CpuInfo Info { get; }

    /// <summary>Fills one entry per logical processor, in processor order. False means the read failed and nothing was written.</summary>
    /// <param name="totalUtility">Whole-machine utility, 0 to 100.</param>
    /// <param name="performancePercent">The processors' current speed as a percentage of base; above 100 when boosting.</param>
    bool Read(Span<CpuLoad> perProcessor, out double totalUtility, out double performancePercent);

    TimeSpan UpTime();
}
