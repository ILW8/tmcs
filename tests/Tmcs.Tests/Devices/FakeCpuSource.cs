using Tmcs.Devices.Cpu;

namespace Tmcs.Tests.Devices;

/// <summary>Four processors at a 4500 MHz base; returns whatever the test sets.</summary>
internal sealed class FakeCpuSource : ICpuSource
{
    public CpuInfo Info { get; } = new("Fake CPU @ 4.50GHz ", 4500, 1, 2, 4, "Enabled", 65536, 1048576, 8388608);
    public CpuLoad[] Loads { get; set; } = new CpuLoad[4];
    public double Total { get; set; }
    public double Performance { get; set; } = 100;
    public bool Succeeds { get; set; } = true;
    public TimeSpan Up { get; set; } = TimeSpan.FromSeconds(5);

    public bool Read(Span<CpuLoad> perProcessor, out double totalUtility, out double performancePercent)
    {
        totalUtility = Total; performancePercent = Performance;
        if (!Succeeds) return false;
        Loads.CopyTo(perProcessor);
        return true;
    }

    public TimeSpan UpTime() => Up;
    public void Dispose() { }
}
