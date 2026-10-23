using Tmcs.Processes;

namespace Tmcs.Details;

/// <summary>Carries a selection across re-sorts and refreshes by process identity instead of row index.</summary>
internal static class SelectionMap
{
    /// <summary>
    /// Replaces the contents of <paramref name="into"/> with the identities of the rows at <paramref name="indices"/>.
    /// Clearing first keeps identities of exited processes from piling up in a set the caller reuses. Indices outside the rows are skipped.
    /// </summary>
    public static void Capture(IReadOnlyList<ProcessRow> rows, IEnumerable<int> indices, HashSet<ProcessIdentity> into)
    {
        into.Clear();
        foreach (var i in indices)
            if ((uint)i < (uint)rows.Count)
                into.Add(rows[i].Id);
    }

    /// <summary>The indices, ascending, of the rows whose identity is in <paramref name="selected"/>. Selected processes that exited are dropped.</summary>
    public static List<int> Restore(IReadOnlyList<ProcessRow> rows, IReadOnlySet<ProcessIdentity> selected)
    {
        var result = new List<int>();
        if (selected.Count == 0) return result;
        for (var i = 0; i < rows.Count; i++)
            if (selected.Contains(rows[i].Id))
                result.Add(i);
        return result;
    }
}
