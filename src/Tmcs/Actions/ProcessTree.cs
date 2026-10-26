using Tmcs.Processes;

namespace Tmcs.Actions;

/// <summary>The order End process tree ends processes in, and how their outcomes combine. No Win32: it reads only the snapshot and calls only the endTask it is given.</summary>
internal static class ProcessTree
{
    /// <summary>
    /// <paramref name="root"/> and all its descendants in <paramref name="rows"/>, each descendant before its ancestors (a post-order),
    /// so the root comes last. A row is a child of a process only when it names that process's PID as its parent and started after it:
    /// a row that started earlier had an older process with the same PID as its parent, and is not in the tree. A process is expanded
    /// only when <paramref name="rows"/> holds it with its exact identity: a root with no row, or whose PID's row has another creation
    /// time, exited and maybe had its PID reused, and the rows naming that PID as their parent may be the new process's children. Such
    /// a root comes back alone.
    /// </summary>
    public static IReadOnlyList<ProcessIdentity> OrderForEnding(ProcessIdentity root, IReadOnlyList<ProcessRow> rows)
    {
        var byParent = rows.ToLookup(r => r.ParentPid);
        var known = rows.Select(r => r.Id).ToHashSet();
        var order = new List<ProcessIdentity>();
        var seen = new HashSet<ProcessIdentity>();

        // Iterative post-order: a node is emitted when it is popped the second time, after all its children were emitted.
        var stack = new Stack<(ProcessIdentity Id, bool ChildrenDone)>();
        stack.Push((root, false));
        seen.Add(root);
        while (stack.Count > 0)
        {
            var (id, childrenDone) = stack.Pop();
            if (childrenDone)
            {
                order.Add(id);
                continue;
            }
            stack.Push((id, true));
            if (!known.Contains(id)) continue;
            foreach (var child in byParent[id.Pid])
                if (child.Id.CreateTime > id.CreateTime && seen.Add(child.Id))
                    stack.Push((child.Id, false));
        }
        return order;
    }

    /// <summary>
    /// Ends <paramref name="root"/>'s tree with <paramref name="endTask"/>, in <see cref="OrderForEnding"/>'s order, and returns the first
    /// outcome that needs a dialog, else Success. A root Task Manager refuses outright (PID 0, 1, 4 or 5) is refused here, before
    /// anything is opened, so none of its descendants is ended (reference 3.2 step 1). A descendant that is
    /// <see cref="ActionOutcome.IsGone"/> exited (or its PID was reused) after the snapshot, which is no failure of End process tree,
    /// so it is skipped silently. The root keeps the normal rules.
    /// </summary>
    public static ActionOutcome EndEach(ProcessIdentity root, IReadOnlyList<ProcessRow> rows, Func<ProcessIdentity, ActionOutcome> endTask)
    {
        if (ActionOutcome.IsRefusedPid(root.Pid)) return ActionOutcome.NotValidForThisProcess;

        var first = ActionOutcome.Success;
        foreach (var id in OrderForEnding(root, rows))
        {
            var outcome = endTask(id);
            if (outcome.IsGone && id != root) continue;
            if (outcome.ShowDialog && !first.ShowDialog) first = outcome;
        }
        return first;
    }
}
