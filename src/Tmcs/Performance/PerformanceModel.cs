using Tmcs.Devices;
using Tmcs.Devices.Cpu;
using Tmcs.Devices.Disk;
using Tmcs.Devices.Gpu;
using Tmcs.Devices.Memory;
using Tmcs.Devices.Network;
using Tmcs.Processes;

namespace Tmcs.Performance;

/// <summary>
/// Every device, read once per sample of the form's clock and at no other time: nothing here runs on its own, so Paused reads
/// nothing, and a rescan happens only at the start of a sample. A rescan keeps the device object, and with it the history and
/// the rate baseline, for every key still listed: the address notification fires on every DHCP renewal, and recreating the
/// adapter would wipe its graph. Steady sampling allocates nothing.
/// </summary>
internal sealed class PerformanceModel : IDisposable
{
    readonly DeviceSources _sources;
    readonly List<Device> _devices = [];
    readonly List<DiskDevice> _disks = [];
    readonly List<NetworkDevice> _adapters = [];
    readonly List<GpuDevice> _gpus = [];

    /// <summary>One entry per GPU, in <see cref="_gpus"/>' order; replaced only when the GPU count changes.</summary>
    GpuCounters[] _gpuCounters = [];

    bool _rescanDisks, _rescanAdapters, _rescanGpus;

    public PerformanceModel(DeviceSources sources)
    {
        _sources = sources;
        Cpu = new CpuDevice(sources.Cpu);
        Memory = new MemoryDevice(sources.Memory);
        RescanDisks();
        RescanAdapters();
        RescanGpus();
        RebuildDevices();
    }

    public CpuDevice Cpu { get; }
    public MemoryDevice Memory { get; }

    /// <summary>
    /// In sidebar order: CPU, Memory, the disks, the adapters, then the GPUs, each kind in its source's order. Hidden adapters are
    /// here and sampled like the others, so showing them shows real graphs; only the sidebar leaves them out.
    /// </summary>
    public IReadOnlyList<Device> Devices => _devices;

    /// <summary>Raised inside <see cref="Sample"/>, before any device samples, when a rescan added, removed or reordered devices.</summary>
    public event Action? DevicesChanged;

    /// <summary>For the diagnostics log.</summary>
    public int GpuInstances => _sources.Gpu.TrackedInstances;

    /// <summary>
    /// A device arrived or left. Only sets flags, so the next <see cref="Sample"/> re-enumerates the disks and GPUs; the adapters
    /// have the network notifications.
    /// </summary>
    public void MarkDevicesChanged() => _rescanDisks = _rescanGpus = true;

    public void Sample(long time100Ns, IReadOnlyList<RawProcess> snapshot)
    {
        var changed = false;
        if (_rescanDisks) changed |= RescanDisks();
        if (_rescanAdapters || _sources.Network.Changed) changed |= RescanAdapters();
        if (_rescanGpus) changed |= RescanGpus();
        if (changed)
        {
            RebuildDevices();
            DevicesChanged?.Invoke();
        }

        Cpu.Sample(time100Ns, snapshot);
        Memory.Sample(time100Ns, snapshot);

        // A failed read means the device is gone or changed: its kind is listed again at the start of the next sample.
        for (var i = 0; i < _disks.Count; i++)
        {
            _disks[i].Sample(time100Ns, snapshot);
            if (_disks[i].Failed) _rescanDisks = true;
        }
        for (var i = 0; i < _adapters.Count; i++)
        {
            _adapters[i].Sample(time100Ns, snapshot);
            if (_adapters[i].Failed) _rescanAdapters = true;
        }

        if (_sources.Gpu.ReadAll(_gpuCounters))
        {
            for (var i = 0; i < _gpus.Count; i++) _gpus[i].Accept(_gpuCounters[i], time100Ns);
        }
        else if (_gpus.Count > 0)
        {
            // With no GPU listed there is nothing to rescan: the real source lists none when its counter can't open, and a
            // rescan every tick would run the SetupAPI enumeration every tick.
            _rescanGpus = true;
        }
    }

    public void Dispose() => _sources.Dispose();

    bool RescanDisks()
    {
        _rescanDisks = false;
        var source = _sources.Disks;
        return Rescan(_disks, source.Enumerate(), (d, info) => d.Info.Number == info.Number,
            info => new DiskDevice(source, info), (d, info) => d.Update(info));
    }

    bool RescanAdapters()
    {
        _rescanAdapters = false;
        var source = _sources.Network;
        return Rescan(_adapters, source.Enumerate(), (a, info) => a.Info.InterfaceGuid == info.InterfaceGuid,
            info => new NetworkDevice(source, info), (a, info) => a.Update(info));
    }

    bool RescanGpus()
    {
        _rescanGpus = false;
        var changed = Rescan(_gpus, _sources.Gpu.Enumerate(), (g, info) => g.Info.Luid == info.Luid,
            info => new GpuDevice(info), (g, info) => g.Update(info));
        if (_gpuCounters.Length != _gpus.Count) _gpuCounters = new GpuCounters[_gpus.Count];
        return changed;
    }

    /// <summary>
    /// Refills <paramref name="devices"/> in the listing's order. A device whose key is still listed is kept and given the new
    /// info; a new key gets a new device; a key no longer listed is dropped. Devices are matched on what their key is made of.
    /// </summary>
    /// <returns>True when a device was added, removed or moved.</returns>
    static bool Rescan<TDevice, TInfo>(List<TDevice> devices, IReadOnlyList<TInfo> listed, Func<TDevice, TInfo, bool> same,
        Func<TInfo, TDevice> create, Action<TDevice, TInfo> update) where TDevice : Device
    {
        var old = devices.ToArray();
        devices.Clear();
        var changed = old.Length != listed.Count;
        foreach (var info in listed)
        {
            var device = Array.Find(old, d => same(d, info));
            if (device is null) device = create(info);
            else update(device, info);
            changed |= devices.Count >= old.Length || !ReferenceEquals(old[devices.Count], device);
            devices.Add(device);
        }
        return changed;
    }

    void RebuildDevices()
    {
        _devices.Clear();
        _devices.Add(Cpu);
        _devices.Add(Memory);
        _devices.AddRange(_disks);
        _devices.AddRange(_adapters);
        _devices.AddRange(_gpus);
    }
}
