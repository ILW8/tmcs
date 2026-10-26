using Tmcs.Performance;
using Tmcs.Processes;

namespace Tmcs.Devices;

/// <summary>In sidebar order.</summary>
internal enum DeviceKind { Cpu, Memory, Disk, Network, Gpu }

/// <summary>One sidebar entry's data: what the pane and sidebar show, and the history its graphs draw.</summary>
internal abstract class Device(string key, DeviceKind kind, int seriesCount)
{
    /// <summary>Stable across rescans, so per-device state survives them.</summary>
    public string Key { get; } = key;
    public DeviceKind Kind { get; } = kind;
    public History History { get; } = new(seriesCount);

    /// <summary>The sidebar title, e.g. <c>CPU</c>, <c>Disk 0 (C:)</c>, <c>Ethernet</c>, <c>GPU 0</c>.</summary>
    public abstract string Title { get; }

    /// <summary>The pane heading's left text (<c>GPU</c> for a GPU).</summary>
    public virtual string PaneTitle => Title;

    /// <summary>The pane heading's right text: processor name, total memory, disk model, adapter description, GPU name.</summary>
    public abstract string Subtitle { get; }

    /// <summary>Reads the device's counters once and appends one point to <see cref="History"/>. Only CPU and Memory use the snapshot.</summary>
    public abstract void Sample(long time100Ns, IReadOnlyList<RawProcess> snapshot);

    /// <summary>True when the latest read failed in a way that means the device is gone or changed; the model re-enumerates that kind on the next tick.</summary>
    public virtual bool Failed => false;
}
