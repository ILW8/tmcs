using Tmcs.Devices.Gpu;

namespace Tmcs.Tests.Devices;

/// <summary>Lists <see cref="Gpus"/> and fills <see cref="Counters"/> for each GPU of the last listing, as the real source does; counts its calls.</summary>
internal sealed class FakeGpuSource : IGpuSource
{
    int _listed;

    public IReadOnlyList<GpuInfo> Gpus { get; set; } = [];
    public GpuCounters Counters { get; set; }
    public bool Succeeds { get; set; } = true;

    /// <summary>Makes every read throw, as a source breaking its contract would.</summary>
    public bool Throws { get; set; }

    /// <summary>Makes every listing throw, as a source breaking its contract would.</summary>
    public bool ListingThrows { get; set; }

    public int TrackedInstances { get; set; }

    public int Enumerations { get; private set; }
    public int Reads { get; private set; }

    public IReadOnlyList<GpuInfo> Enumerate()
    {
        Enumerations++;
        if (ListingThrows) throw new InvalidOperationException("fake GPU listing");
        _listed = Gpus.Count;
        return [.. Gpus];
    }

    public bool ReadAll(Span<GpuCounters> perGpu)
    {
        Reads++;
        if (Throws) throw new InvalidOperationException("fake GPU read");
        if (!Succeeds || perGpu.Length < _listed) return false;
        perGpu[.._listed].Fill(Counters);
        return true;
    }

    public void Dispose() { }
}
