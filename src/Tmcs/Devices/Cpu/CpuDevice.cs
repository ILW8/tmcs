using Tmcs.Processes;

namespace Tmcs.Devices.Cpu;

/// <summary>Series 0 is the total utility; <c>1 + 2i</c> is processor i's utility and <c>2 + 2i</c> its privileged (kernel) utility.</summary>
internal sealed class CpuDevice(ICpuSource source) : Device("cpu", DeviceKind.Cpu, 1 + 2 * source.Info.LogicalProcessors)
{
    readonly CpuLoad[] _loads = new CpuLoad[source.Info.LogicalProcessors];
    readonly float[] _values = new float[1 + 2 * source.Info.LogicalProcessors];

    public CpuInfo Info { get; } = source.Info;

    public override string Title => "CPU";
    public override string Subtitle => Info.Name;

    public double Utilization { get; private set; }
    public double SpeedMhz { get; private set; }
    public int Processes { get; private set; }
    public int Threads { get; private set; }
    public int Handles { get; private set; }
    public TimeSpan UpTime { get; private set; }

    public override void Sample(long time100Ns, IReadOnlyList<RawProcess> snapshot)
    {
        // These come from the snapshot and the boot time, not PDH, so a failed PDH read doesn't freeze them. The snapshot's entry
        // count includes System Idle Process, as every system source does.
        int threads = 0, handles = 0;
        for (var i = 0; i < snapshot.Count; i++)
        {
            threads += snapshot[i].Threads;
            handles += snapshot[i].Handles;
        }
        Processes = snapshot.Count;
        Threads = threads;
        Handles = handles;
        UpTime = source.UpTime();

        if (!source.Read(_loads, out var total, out var performance)) return;

        _values[0] = (float)total;
        for (var i = 0; i < _loads.Length; i++)
        {
            _values[1 + 2 * i] = _loads[i].Utility;
            _values[2 + 2 * i] = _loads[i].Privileged;
        }
        History.Add(time100Ns, _values);

        Utilization = total;
        SpeedMhz = performance * Info.BaseMhz / 100;
    }
}
