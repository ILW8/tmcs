using Tmcs.Processes;

namespace Tmcs.Details;

internal static class RowSorter
{
    /// <summary>
    /// Sorts by <paramref name="column"/>. Ties go to Name, then PID, both ascending whatever the direction. That makes the order total,
    /// which <see cref="List{T}.Sort(Comparison{T})"/> needs because it is not stable on its own, so rows never swap places between ticks.
    /// </summary>
    public static void Sort(List<ProcessRow> rows, DetailsColumn column, bool descending)
    {
        var compare = DetailsColumns.Get(column).Compare;
        var byName = DetailsColumns.Get(DetailsColumn.Name).Compare;
        rows.Sort((a, b) =>
        {
            var c = descending ? compare(b, a) : compare(a, b);
            if (c != 0) return c;
            c = byName(a, b);
            return c != 0 ? c : a.Id.Pid.CompareTo(b.Id.Pid);
        });
    }
}
