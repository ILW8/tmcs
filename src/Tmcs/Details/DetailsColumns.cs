namespace Tmcs.Details;

/// <summary>The 11 Details columns with their titles and default widths. The delegates are static and built once, so the comparisons and the cell formatters allocate nothing.</summary>
internal static class DetailsColumns
{
    static readonly ColumnDef[] Defs =
    [
        new(DetailsColumn.Name, "Name", 312, true, HorizontalAlignment.Left,
            static (r, d, out n) => ValueFormat.TryCopy(r.Name, d, out n),
            static (a, b) => CompareText(a.Name, b.Name)),
        new(DetailsColumn.Pid, "PID", 49, true, HorizontalAlignment.Right,
            static (r, d, out n) => ValueFormat.TryFormatPid(r.Id.Pid, d, out n),
            static (a, b) => a.Id.Pid.CompareTo(b.Id.Pid)),
        new(DetailsColumn.Status, "Status", 100, true, HorizontalAlignment.Left,
            static (r, d, out n) => ValueFormat.TryFormatStatus(r.Status, d, out n),
            static (a, b) => ((int)a.Status).CompareTo((int)b.Status)),
        new(DetailsColumn.UserName, "User name", 80, true, HorizontalAlignment.Left,
            static (r, d, out n) => ValueFormat.TryCopy(r.Info.UserName ?? "", d, out n),
            static (a, b) => CompareText(a.Info.UserName ?? "", b.Info.UserName ?? "")),
        new(DetailsColumn.Cpu, "CPU", 40, true, HorizontalAlignment.Right,
            static (r, d, out n) => ValueFormat.TryFormatCpu(r.CpuPercent, d, out n),
            static (a, b) => a.CpuPercent.CompareTo(b.CpuPercent)),
        new(DetailsColumn.ActivePrivateWorkingSet, "Memory (active private working set)", 108, true, HorizontalAlignment.Right,
            static (r, d, out n) => ValueFormat.TryFormatKilobytes(r.PrivateWorkingSet, d, out n),
            static (a, b) => a.PrivateWorkingSet.CompareTo(b.PrivateWorkingSet)),
        new(DetailsColumn.CommandLine, "Command line", 1033, true, HorizontalAlignment.Left,
            static (r, d, out n) => ValueFormat.TryCopy(r.Info.CommandLine ?? "", d, out n),
            static (a, b) => CompareText(a.Info.CommandLine ?? "", b.Info.CommandLine ?? "")),
        new(DetailsColumn.UacVirtualization, "UAC virtualization", 100, true, HorizontalAlignment.Left,
            static (r, d, out n) => ValueFormat.TryFormatUac(r.Info.Uac, d, out n),
            static (a, b) => ((int)a.Info.Uac).CompareTo((int)b.Info.Uac)),
        new(DetailsColumn.Threads, "Threads", 34, false, HorizontalAlignment.Right,
            static (r, d, out n) => ValueFormat.TryFormatCount(r.Threads, d, out n),
            static (a, b) => a.Threads.CompareTo(b.Threads)),
        new(DetailsColumn.Handles, "Handles", 34, false, HorizontalAlignment.Right,
            static (r, d, out n) => ValueFormat.TryFormatCount(r.Handles, d, out n),
            static (a, b) => a.Handles.CompareTo(b.Handles)),
        new(DetailsColumn.CommitSize, "Commit size", 80, false, HorizontalAlignment.Right,
            static (r, d, out n) => ValueFormat.TryFormatKilobytes(r.CommitSize, d, out n),
            static (a, b) => a.CommitSize.CompareTo(b.CommitSize)),
    ];

    /// <summary>All columns in <see cref="DetailsColumn"/> order.</summary>
    public static IReadOnlyList<ColumnDef> All => Defs;

    public static ColumnDef Get(DetailsColumn id) => Defs[(int)id];

    // string.Compare with a StringComparison does not allocate; the StringComparer.CurrentCultureIgnoreCase property creates a new comparer on every access.
    static int CompareText(string a, string b) => string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
}
