using Tmcs.Processes;

namespace Tmcs.Details;

internal static class RowSearch
{
    /// <summary>
    /// Type-to-jump: the first row at or after <paramref name="startIndex"/> whose name starts with <paramref name="prefix"/>
    /// (case-insensitive), wrapping past the end. A <paramref name="startIndex"/> that is negative or at or past the end of the rows
    /// begins at the first row. Returns -1 when nothing matches, there are no rows, or the prefix is empty.
    /// </summary>
    public static int FindByPrefix(IReadOnlyList<ProcessRow> rows, string prefix, int startIndex)
    {
        var count = rows.Count;
        if (count == 0 || prefix.Length == 0) return -1;
        var start = (uint)startIndex < (uint)count ? startIndex : 0;
        for (var i = 0; i < count; i++)
        {
            var index = start + i;
            if (index >= count) index -= count;
            if (rows[index].Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)) return index;
        }
        return -1;
    }
}
