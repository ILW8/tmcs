using Tmcs.Processes;

namespace Tmcs.Actions;

/// <summary>The order End process tree ends processes in. Pure: it reads only the snapshot it is given.</summary>
internal static class ProcessTree
{
    /// <summary>
    /// <paramref name="root"/> and all its descendants in <paramref name="rows"/>, each descendant before its ancestors (a post-order),
    /// so the root comes last. A row is a child of a process only when it names that process's PID as its parent and started after it:
    /// a row that started earlier had an older process with the same PID as its parent, and is not in the tree.
    /// </summary>
    public static IReadOnlyList<ProcessIdentity> OrderForEnding(ProcessIdentity root, IReadOnlyList<ProcessRow> rows)
    {
        var byParent = rows.ToLookup(r => r.ParentPid);
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
            foreach (var child in byParent[id.Pid])
                if (child.Id.CreateTime > id.CreateTime && seen.Add(child.Id))
                    stack.Push((child.Id, false));
        }
        return order;
    }
}
