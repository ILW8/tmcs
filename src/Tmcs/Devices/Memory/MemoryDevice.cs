using Tmcs.Performance;
using Tmcs.Processes;

namespace Tmcs.Devices.Memory;

/// <summary>Series 0 is the memory in use, in bytes.</summary>
internal sealed class MemoryDevice(IMemorySource source) : Device("memory", DeviceKind.Memory, 1)
{
    const string CompressionName = "Memory Compression";
    const int SystemPid = 4;

    readonly float[] _values = new float[1];

    public MemoryInfo Info { get; } = source.Info;

    public override string Title => "Memory";
    public override string Subtitle => PerfFormat.Size(Info.Installed);

    /// <summary>Total physical memory minus the standby, free and modified lists.</summary>
    public long InUse { get; private set; }

    /// <summary>The working set of the Memory Compression process.</summary>
    public long Compressed { get; private set; }

    public long Available { get; private set; }
    public long Committed { get; private set; }
    public long CommitLimit { get; private set; }
    public long Cached { get; private set; }
    public long PagedPool { get; private set; }
    public long NonPagedPool { get; private set; }
    public long Modified { get; private set; }
    public long Standby { get; private set; }
    public long Free { get; private set; }

    /// <summary>
    /// Total minus available, which is In use plus Modified; it matches Task Manager's sidebar, not its In use figure.
    /// </summary>
    public string SidebarText
    {
        get
        {
            var total = Info.TotalPhysical;
            var used = total - Available;
            var percent = total == 0 ? 0 : (double)used / total * 100;
            return PerfFormat.Pair(used, total, "GB", 1 << 30) + " (" + PerfFormat.Percent(percent) + ")";
        }
    }

    public override void Sample(long time100Ns, IReadOnlyList<RawProcess> snapshot)
    {
        if (!source.Read(out var c)) return;

        Available = c.Available;
        Committed = c.Committed;
        CommitLimit = c.CommitLimit;
        PagedPool = c.PagedPool;
        NonPagedPool = c.NonPagedPool;
        Modified = c.Modified;
        Standby = c.Standby;
        Free = c.Free;
        Cached = c.Standby + c.Modified;
        InUse = Info.TotalPhysical - (c.Standby + c.Free + c.Modified);

        long compressed = 0;
        for (var i = 0; i < snapshot.Count; i++)
        {
            var p = snapshot[i];
            if (p.ParentPid == SystemPid && p.Name == CompressionName) { compressed = p.WorkingSet; break; }
        }
        Compressed = compressed;

        _values[0] = InUse;
        History.Add(time100Ns, _values);
    }
}
