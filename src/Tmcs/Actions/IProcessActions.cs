using Tmcs.Processes;

namespace Tmcs.Actions;

/// <summary>
/// The Details tab's actions on a process. Every call names a <see cref="ProcessIdentity"/>, never a bare PID: the process is opened,
/// then its creation time is compared with the identity's, so a reused PID is never acted on (reference 7).
/// </summary>
internal interface IProcessActions
{
    /// <summary>Whether tmcs itself runs elevated (<c>TokenElevation</c>), read once.</summary>
    bool IsElevated { get; }

    /// <summary>Whether the process has its break-on-termination flag set, so ending it stops Windows. False when it can't be read.</summary>
    bool IsCritical(ProcessIdentity id);

    /// <summary>Ends the process with exit code 1 (reference 3.2, the Details-row path with no window pre-pass).</summary>
    ActionOutcome EndTask(ProcessIdentity id);

    /// <summary>
    /// Ends the process and its descendants in <paramref name="snapshot"/>, each descendant before its parent
    /// (<see cref="ProcessTree.OrderForEnding"/>). The outcome is the first one that needs a dialog, else Success.
    /// </summary>
    ActionOutcome EndProcessTree(ProcessIdentity id, IReadOnlyList<ProcessRow> snapshot);

    PrioritySet SetPriority(ProcessIdentity id, ProcessPriority priority);

    /// <summary>Null when the process can't be opened or its masks can't be read.</summary>
    AffinityInfo? GetAffinity(ProcessIdentity id);

    ActionOutcome SetAffinity(ProcessIdentity id, nuint mask);
}
