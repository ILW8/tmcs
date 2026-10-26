namespace Tmcs.Processes;

/// <summary>
/// The live process list, keyed by <see cref="ProcessIdentity"/>. Each <see cref="Apply"/> diffs one snapshot into rows:
/// exits leave the table on the same tick and a reused PID (new creation time) becomes a new row.
/// Steady-state ticks allocate nothing beyond rows for newly seen processes.
/// </summary>
internal sealed class ProcessTable
{
    readonly IProcessInfoSource _info;
    readonly int _logicalProcessors;
    readonly Dictionary<ProcessIdentity, ProcessRow> _byId = new();
    readonly List<ProcessRow> _rows = new();
    readonly HashSet<ProcessIdentity> _seen = new();
    readonly List<int> _newIndexes = new(); // indexes into the snapshot of processes that have no row yet
    long _lastTimestamp;

    public ProcessTable(IProcessInfoSource info, int logicalProcessors)
    {
        _info = info;
        _logicalProcessors = logicalProcessors;
    }

    /// <summary>Unordered; the view sorts. The list is the live backing store, so do not hold it across <see cref="Apply"/>.</summary>
    public IReadOnlyList<ProcessRow> Rows => _rows;

    public int Count => _rows.Count;

    public event Action<ProcessRow>? Added;
    public event Action<ProcessRow>? Removed;

    /// <param name="snapshot">Every process currently alive, as read by the snapshot source.</param>
    /// <param name="timestamp100Ns">Monotonic time of this snapshot in 100 ns ticks; the CPU wall delta is taken against the previous call.</param>
    public void Apply(List<RawProcess> snapshot, long timestamp100Ns)
    {
        long wallDelta = timestamp100Ns - _lastTimestamp;
        _lastTimestamp = timestamp100Ns;
        _seen.Clear();
        _newIndexes.Clear();

        // Update the rows that survive and note which snapshot entries still need a row.
        for (int i = 0; i < snapshot.Count; i++)
        {
            var raw = snapshot[i];
            var id = new ProcessIdentity(raw.Pid, raw.CreateTime);
            if (!_seen.Add(id))
                continue; // duplicate identity in one snapshot: keep the first

            if (_byId.TryGetValue(id, out var row))
            {
                long cpuDelta = raw.KernelTime + raw.UserTime - (row.KernelTime + row.UserTime);
                row.CpuPercent = CpuMath.Percent(cpuDelta, wallDelta, _logicalProcessors);
                CopyLive(row, in raw);
            }
            else
            {
                _newIndexes.Add(i);
            }
        }

        // Remove exited rows first (swap-remove: order is irrelevant, the view sorts). Walking backwards means the row
        // swapped into slot i came from the end and has already been checked.
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            var row = _rows[i];
            if (_seen.Contains(row.Id))
                continue;

            int last = _rows.Count - 1;
            _rows[i] = _rows[last];
            _rows.RemoveAt(last);
            _byId.Remove(row.Id);
            Removed?.Invoke(row);
        }

        // Then add new rows, so a reused PID raises Removed (old instance) before Added (new instance).
        foreach (int index in _newIndexes)
        {
            var raw = snapshot[index];
            var id = new ProcessIdentity(raw.Pid, raw.CreateTime);
            var row = new ProcessRow(id, raw.Name, raw.ParentPid, _info.Query(id));
            CopyLive(row, in raw);
            _byId.Add(id, row);
            _rows.Add(row);
            Added?.Invoke(row);
        }
    }

    /// <summary>Marks rows whose PID is in <paramref name="hungPids"/> as not responding. A suspended process stays suspended.</summary>
    public void ApplyHung(IReadOnlySet<int> hungPids)
    {
        foreach (var row in _rows)
        {
            if (row.Status != ProcessStatus.Suspended && hungPids.Contains(row.Id.Pid))
                row.Status = ProcessStatus.NotResponding;
        }
    }

    static void CopyLive(ProcessRow row, in RawProcess raw)
    {
        row.Status = raw.IsSuspended ? ProcessStatus.Suspended : ProcessStatus.Running;
        row.PrivateWorkingSet = raw.PrivateWorkingSet;
        row.CommitSize = raw.CommitSize;
        row.Threads = raw.Threads;
        row.Handles = raw.Handles;
        row.KernelTime = raw.KernelTime;
        row.UserTime = raw.UserTime;
        row.BasePriority = raw.BasePriority;
    }
}
