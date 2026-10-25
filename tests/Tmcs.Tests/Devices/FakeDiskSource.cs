using Tmcs.Devices.Disk;

namespace Tmcs.Tests.Devices;

/// <summary>Lists <see cref="Disks"/> and reads <see cref="Counters"/> for any disk; counts its calls.</summary>
internal sealed class FakeDiskSource : IDiskSource
{
    public IReadOnlyList<DiskInfo> Disks { get; set; } = [];
    public DiskCounters Counters { get; set; }
    public bool Succeeds { get; set; } = true;

    /// <summary>Makes every read throw, as a source breaking its contract would.</summary>
    public bool Throws { get; set; }

    /// <summary>Makes every listing throw, as a source breaking its contract would.</summary>
    public bool ListingThrows { get; set; }

    /// <summary>Disk numbers whose reads fail even while <see cref="Succeeds"/> is set.</summary>
    public HashSet<int> Failing { get; } = [];

    public int Enumerations { get; private set; }
    public int Reads { get; private set; }

    public IReadOnlyList<DiskInfo> Enumerate()
    {
        Enumerations++;
        if (ListingThrows) throw new InvalidOperationException("fake disk listing");
        return [.. Disks];
    }

    public bool Read(int number, out DiskCounters counters)
    {
        Reads++;
        if (Throws) throw new InvalidOperationException("fake disk read");
        counters = Counters;
        return Succeeds && !Failing.Contains(number);
    }
}
