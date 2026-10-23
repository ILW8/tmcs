namespace Tmcs.Details;

/// <summary>The 11 Details columns with their titles and default widths. The delegates are static and built once, so the comparisons allocate nothing; only the cell text strings are allocated, one per cell read.</summary>
internal static class DetailsColumns
{
    static readonly ColumnDef[] Defs =
    [
        new(DetailsColumn.Name, "Name", 312, true, HorizontalAlignment.Left,
            static r => r.Name,
            static (a, b) => CompareText(a.Name, b.Name)),
        new(DetailsColumn.Pid, "PID", 49, true, HorizontalAlignment.Right,
            static r => r.Id.Pid.ToString(),
            static (a, b) => a.Id.Pid.CompareTo(b.Id.Pid)),
        new(DetailsColumn.Status, "Status", 100, true, HorizontalAlignment.Left,
            static r => ValueFormat.Status(r.Status),
            static (a, b) => ((int)a.Status).CompareTo((int)b.Status)),
        new(DetailsColumn.UserName, "User name", 80, true, HorizontalAlignment.Left,
            static r => r.Info.UserName ?? "",
            static (a, b) => CompareText(a.Info.UserName ?? "", b.Info.UserName ?? "")),
        new(DetailsColumn.Cpu, "CPU", 40, true, HorizontalAlignment.Right,
            static r => ValueFormat.Cpu(r.CpuPercent),
            static (a, b) => a.CpuPercent.CompareTo(b.CpuPercent)),
        new(DetailsColumn.ActivePrivateWorkingSet, "Memory (active private working set)", 108, true, HorizontalAlignment.Right,
            static r => ValueFormat.Kilobytes(r.PrivateWorkingSet),
            static (a, b) => a.PrivateWorkingSet.CompareTo(b.PrivateWorkingSet)),
        new(DetailsColumn.CommandLine, "Command line", 1033, true, HorizontalAlignment.Left,
            static r => r.Info.CommandLine ?? "",
            static (a, b) => CompareText(a.Info.CommandLine ?? "", b.Info.CommandLine ?? "")),
        new(DetailsColumn.UacVirtualization, "UAC virtualization", 100, true, HorizontalAlignment.Left,
            static r => ValueFormat.Uac(r.Info.Uac),
            static (a, b) => ((int)a.Info.Uac).CompareTo((int)b.Info.Uac)),
        new(DetailsColumn.Threads, "Threads", 34, false, HorizontalAlignment.Right,
            static r => ValueFormat.Count(r.Threads),
            static (a, b) => a.Threads.CompareTo(b.Threads)),
        new(DetailsColumn.Handles, "Handles", 34, false, HorizontalAlignment.Right,
            static r => ValueFormat.Count(r.Handles),
            static (a, b) => a.Handles.CompareTo(b.Handles)),
        new(DetailsColumn.CommitSize, "Commit size", 80, false, HorizontalAlignment.Right,
            static r => ValueFormat.Kilobytes(r.CommitSize),
            static (a, b) => a.CommitSize.CompareTo(b.CommitSize)),
    ];

    /// <summary>All columns in <see cref="DetailsColumn"/> order.</summary>
    public static IReadOnlyList<ColumnDef> All => Defs;

    public static ColumnDef Get(DetailsColumn id) => Defs[(int)id];

    // string.Compare with a StringComparison does not allocate; the StringComparer.CurrentCultureIgnoreCase property creates a new comparer on every access.
    static int CompareText(string a, string b) => string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
}
