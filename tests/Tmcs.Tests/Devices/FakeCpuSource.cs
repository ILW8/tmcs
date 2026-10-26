using Tmcs.Devices.Cpu;

namespace Tmcs.Tests.Devices;

/// <summary>By default four processors at a 4500 MHz base; returns whatever the test sets and counts its reads.</summary>
internal sealed class FakeCpuSource(CpuInfo info) : ICpuSource
{
    public FakeCpuSource() : this(new CpuInfo("Fake CPU @ 4.50GHz ", 4500, 1, 2, 4, "Enabled", 65536, 1048576, 8388608))
    {
    }

    /// <summary>The owner's Ryzen 9 7950X as capture 01 shows it, with <paramref name="processors"/> logical processors.</summary>
    public static CpuInfo Ryzen(int processors = 32) =>
        new("AMD Ryzen 9 7950X 16-Core Processor ", 4500, 1, 16, processors, "Enabled", 1L << 20, 16L << 20, 64L << 20);

    public CpuInfo Info { get; } = info;
    public CpuLoad[] Loads { get; set; } = new CpuLoad[info.LogicalProcessors];
    public double Total { get; set; }
    public double Time { get; set; }
    public double Performance { get; set; } = 100;
    public bool Succeeds { get; set; } = true;

    /// <summary>Makes every read throw, as a source breaking its contract would.</summary>
    public bool Throws { get; set; }

    public TimeSpan Up { get; set; } = TimeSpan.FromSeconds(5);
    public int Reads { get; private set; }

    public bool Read(Span<CpuLoad> perProcessor, out double totalUtility, out double totalTime, out double performancePercent)
    {
        Reads++;
        if (Throws) throw new InvalidOperationException("fake CPU read");
        totalUtility = Total; totalTime = Time; performancePercent = Performance;
        if (!Succeeds) return false;
        Loads.CopyTo(perProcessor);
        return true;
    }

    public TimeSpan UpTime() => Up;
    public void Dispose() { }
}
