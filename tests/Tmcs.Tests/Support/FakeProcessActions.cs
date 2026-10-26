using Tmcs.Actions;
using Tmcs.Processes;

namespace Tmcs.Tests.Support;

/// <summary>
/// Scripted process actions. Every call succeeds unless a test sets a per-PID outcome; <see cref="Calls"/> records each call in order.
/// <see cref="EndProcessTree"/> calls <see cref="EndTask"/> through <see cref="ProcessTree.EndEach"/>, like the real one.
/// </summary>
internal sealed class FakeProcessActions : IProcessActions
{
    /// <summary>What the real <see cref="ProcessActions.EndTask"/> returns when the PID was reused (the identity check failed) or the process exited.</summary>
    public static readonly ActionOutcome ReusedPid = ActionOutcome.Gone;

    /// <summary>What the real <see cref="ProcessActions.EndTask"/> returns when <c>TerminateProcess</c> is denied after a good open: no dialog.</summary>
    public static readonly ActionOutcome AccessDenied = new(ActionOutcome.EAccessDenied, ShowDialog: false);

    public bool IsElevated { get; set; }

    public HashSet<int> CriticalPids { get; } = [];
    public Dictionary<int, ActionOutcome> EndTaskOutcomes { get; } = new();
    public Dictionary<int, PrioritySet> PriorityOutcomes { get; } = new();
    public Dictionary<int, AffinityInfo> Affinities { get; } = new();
    public Dictionary<int, ActionOutcome> AffinityOutcomes { get; } = new();

    public List<(string Action, ProcessIdentity Id)> Calls { get; } = [];

    public bool IsCritical(ProcessIdentity id)
    {
        Calls.Add((nameof(IsCritical), id));
        return CriticalPids.Contains(id.Pid);
    }

    public ActionOutcome EndTask(ProcessIdentity id)
    {
        Calls.Add((nameof(EndTask), id));
        return EndTaskOutcomes.GetValueOrDefault(id.Pid, ActionOutcome.Success);
    }

    public ActionOutcome EndProcessTree(ProcessIdentity id, IReadOnlyList<ProcessRow> snapshot)
    {
        Calls.Add((nameof(EndProcessTree), id));
        return ProcessTree.EndEach(id, snapshot, EndTask);
    }

    /// <summary>
    /// Models a tmcs without <c>SeIncreaseBasePriorityPrivilege</c>: a Realtime request succeeds, but the class reads back as High,
    /// so the result has <see cref="PrioritySet.ReadBackMatched"/> false (reference 4). A per-PID outcome still wins.
    /// </summary>
    public bool RealtimeReadsBackAsHigh { get; set; }

    /// <summary>Each <see cref="SetPriority"/> call's process and requested priority, in order.</summary>
    public List<(ProcessIdentity Id, ProcessPriority Priority)> PrioritySets { get; } = [];

    public PrioritySet SetPriority(ProcessIdentity id, ProcessPriority priority)
    {
        Calls.Add((nameof(SetPriority), id));
        PrioritySets.Add((id, priority));
        if (PriorityOutcomes.TryGetValue(id.Pid, out var scripted)) return scripted;
        return new PrioritySet(ActionOutcome.Success, ReadBackMatched: !(RealtimeReadsBackAsHigh && priority == ProcessPriority.Realtime));
    }

    /// <summary>A scripted failure for <see cref="GetAffinity"/>. A PID with neither this nor an entry in <see cref="Affinities"/> reads as access denied.</summary>
    public Dictionary<int, ActionOutcome> AffinityReadOutcomes { get; } = new();

    public AffinityRead GetAffinity(ProcessIdentity id)
    {
        Calls.Add((nameof(GetAffinity), id));
        if (AffinityReadOutcomes.TryGetValue(id.Pid, out var failure)) return new(failure, default);
        return Affinities.TryGetValue(id.Pid, out var info)
            ? new(ActionOutcome.Success, info)
            : new(new(ActionOutcome.EAccessDenied, ShowDialog: true), default);
    }

    /// <summary>Each <see cref="SetAffinity"/> call's process and requested mask, in order.</summary>
    public List<(ProcessIdentity Id, nuint Mask)> AffinitySets { get; } = [];

    public ActionOutcome SetAffinity(ProcessIdentity id, nuint mask)
    {
        Calls.Add((nameof(SetAffinity), id));
        AffinitySets.Add((id, mask));
        return AffinityOutcomes.GetValueOrDefault(id.Pid, ActionOutcome.Success);
    }
}
