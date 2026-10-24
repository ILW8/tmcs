using Tmcs.Devices.Gpu;

namespace Tmcs.Tests.Devices;

/// <summary>Lists <see cref="Gpus"/> and fills <see cref="Counters"/> for each GPU of the last listing, as the real source does; counts its calls.</summary>
internal sealed class FakeGpuSource : IGpuSource
{
    int _listed;

    public IReadOnlyList<GpuInfo> Gpus { get; set; } = [];
    public GpuCounters Counters { get; set; }
    public bool Succeeds { get; set; } = true;
    public int TrackedInstances { get; set; }

    public int Enumerations { get; private set; }
    public int Reads { get; private set; }

    public IReadOnlyList<GpuInfo> Enumerate()
    {
        Enumerations++;
        _listed = Gpus.Count;
        return [.. Gpus];
    }

    public bool ReadAll(Span<GpuCounters> perGpu)
    {
        Reads++;
        if (!Succeeds || perGpu.Length < _listed) return false;
        perGpu[.._listed].Fill(Counters);
        return true;
    }

    public void Dispose() { }
}
