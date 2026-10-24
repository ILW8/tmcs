namespace Tmcs.Processes;

/// <summary>
/// Identifies one process instance. A PID alone is not enough: Windows reuses PIDs, so the creation time is part of the key.
/// Per-process state anywhere in the app is keyed by this, never by PID.
/// </summary>
internal readonly record struct ProcessIdentity(int Pid, long CreateTime);
