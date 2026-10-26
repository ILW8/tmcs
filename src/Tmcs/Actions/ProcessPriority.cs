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

/// <summary>The mappings of <see cref="ProcessPriority"/> to the Win32 priority class and from the snapshot's base priority (reference 4).</summary>
internal static class ProcessPriorities
{
    /// <summary>The <c>*_PRIORITY_CLASS</c> value that <c>SetPriorityClass</c> takes and <c>GetPriorityClass</c> returns.</summary>
    public static uint ToClass(ProcessPriority priority) => priority switch
    {
        ProcessPriority.Low => 0x40,            // IDLE_PRIORITY_CLASS
        ProcessPriority.BelowNormal => 0x4000,  // BELOW_NORMAL_PRIORITY_CLASS
        ProcessPriority.Normal => 0x20,         // NORMAL_PRIORITY_CLASS
        ProcessPriority.AboveNormal => 0x8000,  // ABOVE_NORMAL_PRIORITY_CLASS
        ProcessPriority.High => 0x80,           // HIGH_PRIORITY_CLASS
        ProcessPriority.Realtime => 0x100,      // REALTIME_PRIORITY_CLASS
        _ => throw new ArgumentOutOfRangeException(nameof(priority)),
    };

    /// <summary>The class whose base priority is <paramref name="basePriority"/>, or null for a value no class reports.</summary>
    public static ProcessPriority? FromBasePriority(int basePriority) => basePriority switch
    {
        4 => ProcessPriority.Low,
        6 => ProcessPriority.BelowNormal,
        8 => ProcessPriority.Normal,
        10 => ProcessPriority.AboveNormal,
        13 => ProcessPriority.High,
        24 => ProcessPriority.Realtime,
        _ => null,
    };
}
