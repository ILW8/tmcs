namespace Tmcs.Actions;

/// <summary>
/// The six priority classes of the Set priority menu, in ascending order (reference 4). Each maps to a <c>*_PRIORITY_CLASS</c>
/// and to the base priority a process of that class reports.
/// </summary>
internal enum ProcessPriority
{
    /// <summary><c>IDLE_PRIORITY_CLASS</c>, base priority 4.</summary>
    Low,
    /// <summary><c>BELOW_NORMAL_PRIORITY_CLASS</c>, base priority 6.</summary>
    BelowNormal,
    /// <summary><c>NORMAL_PRIORITY_CLASS</c>, base priority 8.</summary>
    Normal,
    /// <summary><c>ABOVE_NORMAL_PRIORITY_CLASS</c>, base priority 10.</summary>
    AboveNormal,
    /// <summary><c>HIGH_PRIORITY_CLASS</c>, base priority 13.</summary>
    High,
    /// <summary><c>REALTIME_PRIORITY_CLASS</c>, base priority 24.</summary>
    Realtime,
}
