using Tmcs.Devices.Memory;

namespace Tmcs.Tests.Devices;

/// <summary>64 GiB of physical memory in a 64 GiB + 356.5 MiB machine; returns whatever the test sets and counts its reads.</summary>
internal sealed class FakeMemorySource : IMemorySource
{
    public const long Gib = 1L << 30;

    public MemoryInfo Info { get; set; } = new(64 * Gib + 713 * (1L << 19), 64 * Gib, 5600, 2, 4, "DIMM");
    public MemoryCounters Counters { get; set; }
    public bool Succeeds { get; set; } = true;
    public int Reads { get; private set; }

    public bool Read(out MemoryCounters counters)
    {
        Reads++;
        counters = Counters;
        return Succeeds;
    }
}
