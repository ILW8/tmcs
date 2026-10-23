namespace Tmcs.Processes;

/// <summary>One process as reported by a single <c>SystemProcessInformation</c> snapshot. Times are in 100 ns ticks.</summary>
internal readonly record struct RawProcess(
    int Pid,
    int ParentPid,
    long CreateTime,
    string Name,
    long KernelTime,
    long UserTime,
    long PrivateWorkingSet,
    long CommitSize,
    int Threads,
    int Handles,
    int SessionId,
    int BasePriority,
    bool IsSuspended);

internal interface IProcessSnapshotSource
{
    /// <summary>Replaces the contents of <paramref name="into"/> with the current process list.</summary>
    void Read(List<RawProcess> into);
}
