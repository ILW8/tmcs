using Tmcs.Devices.Cpu;
using Tmcs.Devices.Disk;
using Tmcs.Devices.Gpu;
using Tmcs.Devices.Memory;
using Tmcs.Devices.Network;

namespace Tmcs.Performance;

/// <summary>
/// The sources the <see cref="PerformanceModel"/> reads. Tests pass fakes. A real source never throws on the tick path, nor from
/// its constructor because something is missing or broken (PDH counters disabled or corrupt, no GPU, the WLAN service stopped):
/// it returns false or an empty list instead and writes the reason once with <c>Debug.WriteLine</c>, so tmcs opens on whatever
/// the machine lacks.
/// </summary>
internal sealed class DeviceSources(ICpuSource cpu, IMemorySource memory, IDiskSource disks, INetworkSource network, IGpuSource gpu) : IDisposable
{
    public ICpuSource Cpu { get; } = cpu;
    public IMemorySource Memory { get; } = memory;
    public IDiskSource Disks { get; } = disks;
    public INetworkSource Network { get; } = network;
    public IGpuSource Gpu { get; } = gpu;

    public static DeviceSources CreateReal() =>
        new(new PdhCpuSource(), new SystemMemorySource(), new SystemDiskSource(), new SystemNetworkSource(), new SystemGpuSource());

    /// <summary>The memory and disk sources hold nothing to release.</summary>
    public void Dispose()
    {
        Cpu.Dispose();
        Network.Dispose();
        Gpu.Dispose();
    }
}
