using Tmcs.Native;
using Tmcs.Processes;

namespace Tmcs.Details;

/// <summary>
/// Keeps a virtual list's selection and focus on the same processes while its rows are replaced and re-sorted.
/// It reads and writes the native item state: enumerating WinForms' <see cref="ListView.SelectedIndices"/> allocates on every call,
/// and every selection change WinForms is told about costs it a <see cref="ListViewItem"/>, so a selection that has not moved is left alone.
/// </summary>
internal sealed class SelectionKeeper(ListView list)
{
    private readonly HashSet<ProcessIdentity> _selected = [];
    private ProcessIdentity? _focused;

    /// <summary>Remembers the selected and focused processes. <paramref name="rows"/> are the rows the list shows now.</summary>
    public void Capture(IReadOnlyList<ProcessRow> rows)
    {
        _selected.Clear();
        _focused = null;
        if (!list.IsHandleCreated) return;
        var handle = list.Handle;
        for (var i = Next(handle, -1, User32.LvniSelected); i >= 0; i = Next(handle, i, User32.LvniSelected))
            if (i < rows.Count)
                _selected.Add(rows[i].Id);
        var focus = Next(handle, -1, User32.LvniFocused);
        if ((uint)focus < (uint)rows.Count) _focused = rows[focus].Id;
    }

    /// <summary>
    /// Selects and focuses the remembered processes. <paramref name="rows"/> are the rows the list shows from now on, already counted
    /// by the list. Processes that are gone drop out of the selection.
    /// </summary>
    public void Restore(IReadOnlyList<ProcessRow> rows)
    {
        if (!list.IsHandleCreated) return;
        var handle = list.Handle;
        var selectedNow = (int)User32.SendMessage(handle, User32.LvmGetSelectedCount, 0, 0);
        if (_selected.Count == 0 && selectedNow == 0 && _focused is null) return;

        var found = 0;
        var unchanged = true;
        var focus = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            var id = rows[i].Id;
            if (id == _focused) focus = i;
            if (!_selected.Contains(id)) continue;
            found++;
            unchanged &= IsSelected(handle, i);
        }

        if (!unchanged || found != selectedNow)
        {
            SetState(handle, -1, 0, User32.LvisSelected);
            for (var i = 0; i < rows.Count; i++)
                if (_selected.Contains(rows[i].Id))
                    SetState(handle, i, User32.LvisSelected, User32.LvisSelected);
        }

        // Keyboard navigation and Shift+click start from the focus and the selection mark, so they move with the process too.
        if (focus >= 0 && Next(handle, -1, User32.LvniFocused) != focus)
        {
            SetState(handle, focus, User32.LvisFocused, User32.LvisFocused);
            User32.SendMessage(handle, User32.LvmSetSelectionMark, 0, focus);
        }
    }

    private static int Next(nint list, int after, nint flags) => (int)User32.SendMessage(list, User32.LvmGetNextItem, after, flags);

    private static bool IsSelected(nint list, int index) => User32.SendMessage(list, User32.LvmGetItemState, index, (nint)User32.LvisSelected) != 0;

    /// <summary>Sets the state bits under <paramref name="mask"/> to <paramref name="state"/> on item <paramref name="index"/>, or on every item when it is -1.</summary>
    private static void SetState(nint list, int index, uint state, uint mask)
    {
        var item = new User32.LvItem { State = state, StateMask = mask };
        User32.SendMessage(list, User32.LvmSetItemState, index, ref item);
    }
}
