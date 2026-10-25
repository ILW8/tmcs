using Tmcs.Devices.Disk;

namespace Tmcs.Tests.Devices;

/// <summary>Lists <see cref="Disks"/> and reads <see cref="Counters"/> for any disk; counts its calls.</summary>
internal sealed class FakeDiskSource : IDiskSource
{
    public IReadOnlyList<DiskInfo> Disks { get; set; } = [];
    public DiskCounters Counters { get; set; }
    public bool Succeeds { get; set; } = true;

    /// <summary>Disk numbers whose reads fail even while <see cref="Succeeds"/> is set.</summary>
    public HashSet<int> Failing { get; } = [];

    public int Enumerations { get; private set; }
    public int Reads { get; private set; }

    public IReadOnlyList<DiskInfo> Enumerate()
    {
        Enumerations++;
        return [.. Disks];
    }

    public bool Read(int number, out DiskCounters counters)
    {
        Reads++;
        counters = Counters;
        return Succeeds && !Failing.Contains(number);
    }
}
