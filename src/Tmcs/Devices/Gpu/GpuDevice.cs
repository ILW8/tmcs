using Tmcs.Performance;
using Tmcs.Processes;

namespace Tmcs.Devices.Gpu;

/// <summary>
/// Series 0 is the utilization, 1 to 4 the 3D, Copy, Video Encode and Video Decode engines, all in percent; 5 and 6 are
/// the dedicated and shared memory in use, in bytes. The key carries the LUID, so a driver restart makes a new GPU.
/// </summary>
internal sealed class GpuDevice(GpuInfo info) : Device($"gpu:{info.Luid:x}", DeviceKind.Gpu, 7)
{
    readonly float[] _values = new float[7];

    public GpuInfo Info { get; private set; } = info;

    public override string Title => $"GPU {Info.Index}";
    public override string PaneTitle => "GPU";
    public override string Subtitle => Info.Name;

    public GpuCounters Counters { get; private set; }

    /// <summary>e.g. <c>(38 °C)</c>, which the sidebar draws after the utilization; null without a temperature.</summary>
    public string? TemperatureText => Counters.TemperatureC is { } t ? "(" + PerfFormat.Celsius(t) + ")" : null;

    /// <summary>A rescan's info for the same LUID: the index moves when another GPU goes. The history is kept.</summary>
    public void Update(GpuInfo info) => Info = info;

    /// <summary>The model reads every GPU with one <see cref="IGpuSource.ReadAll"/> and hands each its entry through <see cref="Accept"/>.</summary>
    public override void Sample(long time100Ns, IReadOnlyList<RawProcess> snapshot) { }

    public void Accept(in GpuCounters counters, long time100Ns)
    {
        Counters = counters;
        _values[0] = counters.Utilization;
        _values[1] = counters.ThreeD;
        _values[2] = counters.Copy;
        _values[3] = counters.VideoEncode;
        _values[4] = counters.VideoDecode;
        _values[5] = counters.DedicatedUsed;
        _values[6] = counters.SharedUsed;
        History.Add(time100Ns, _values);
    }
}
