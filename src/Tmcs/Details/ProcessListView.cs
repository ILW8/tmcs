using System.ComponentModel;
using Tmcs.Native;

namespace Tmcs.Details;

/// <summary>
/// The virtual-mode list behind the Details tab. Row icons come from one native image list that the caller owns
/// (<see cref="SharedImageList"/>); the control never copies or destroys it, so no image is ever added per row.
/// </summary>
internal sealed class ProcessListView : ListView
{
    /// <summary><c>LVS_SHAREIMAGELISTS</c>: the control does not destroy its image lists.</summary>
    private const int LvsShareImageLists = 0x40;

    private const int WmContextMenu = 0x007B;

    private nint _sharedImageList;

    /// <summary>True while <see cref="SetSortArrow"/> writes a header item, whose notification is not a column resize.</summary>
    private bool _writingSortArrow;

    public ProcessListView()
    {
        View = View.Details;
        VirtualMode = true;
        FullRowSelect = true;
        HideSelection = false;
        AllowColumnReorder = true;
        DoubleBuffered = true;
        HeaderStyle = ColumnHeaderStyle.Clickable;
    }

    /// <summary>
    /// A context menu was asked for on the header: the index of the column under the cursor (-1 when none, and always
    /// -1 from the keyboard) and the screen point to show the menu at.
    /// </summary>
    public event Action<int, Point>? HeaderContextMenu;

    /// <summary>
    /// The <c>HIMAGELIST</c> that rows' <see cref="ListViewItem.ImageIndex"/> refers to, attached as the small image list.
    /// The caller keeps ownership and must keep it alive while the control exists. <see cref="ListView.SmallImageList"/> stays null.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // a runtime handle, never designer state
    public nint SharedImageList
    {
        get => _sharedImageList;
        set
        {
            _sharedImageList = value;
            if (IsHandleCreated) AttachImageList();
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= LvsShareImageLists; // WinForms sets it too today; the shared list must not depend on that
            return cp;
        }
    }

    /// <summary>
    /// Shows the sort arrow on <paramref name="columnIndex"/>, pointing down when <paramref name="descending"/>, and clears it
    /// from every other column; -1 clears all. Other format bits, such as alignment, are kept. Header items exist only
    /// while the handle does, so this does nothing before the handle is created, and columns added later start without an arrow.
    /// </summary>
    public void SetSortArrow(int columnIndex, bool descending)
    {
        if (!IsHandleCreated) return;
        var header = HeaderHandle;
        var count = (int)User32.SendMessage(header, User32.HdmGetItemCount, 0, 0);
        for (var i = 0; i < count; i++)
        {
            var item = new User32.HdItem { Mask = User32.HdiFormat };
            if (User32.SendMessage(header, User32.HdmGetItemW, i, ref item) == 0) continue;
            var format = item.Format & ~(User32.HdfSortUp | User32.HdfSortDown);
            if (i == columnIndex) format |= descending ? User32.HdfSortDown : User32.HdfSortUp;
            if (format == item.Format) continue;
            item.Format = format;
            _writingSortArrow = true;
            try { User32.SendMessage(header, User32.HdmSetItemW, i, ref item); }
            finally { _writingSortArrow = false; }
        }
    }

    /// <summary>
    /// Sets <see cref="ListView.VirtualListSize"/> without moving the view: the top row stays on top, unless too few rows
    /// remain below it to fill the page, in which case the last page is shown.
    /// </summary>
    public void SetItemCountKeepScroll(int count)
    {
        if (!IsHandleCreated)
        {
            VirtualListSize = count;
            return;
        }

        var top = GetTopIndex();
        // The native control scrolls back to row 0 here. WinForms' setter restores the top row, except when the handler
        // returns the same ListViewItem object for every row (its check compares references), so check and correct below.
        VirtualListSize = count;
        var now = GetTopIndex();
        if (now == top) return;

        var row = new User32.NativeRect { Left = User32.LvirBounds };
        if (User32.SendMessage(Handle, User32.LvmGetItemRect, now, ref row) == 0) return; // no rows left
        // The control stops at the last page by itself when too few rows remain below the old top row.
        User32.SendMessage(Handle, User32.LvmScroll, 0, (top - now) * (row.Bottom - row.Top));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UxTheme.SetWindowTheme(Handle, "Explorer", null);
        if (_sharedImageList != 0) AttachImageList();
    }

    /// <summary>WinForms reports every header item change as a width change, including the format-only ones made by <see cref="SetSortArrow"/>.</summary>
    protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e)
    {
        if (!_writingSortArrow) base.OnColumnWidthChanged(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmContextMenu && m.WParam != 0 && m.WParam == HeaderHandle)
        {
            RaiseHeaderContextMenu(m.WParam, m.LParam);
            return; // base would also open the row ContextMenuStrip
        }

        base.WndProc(ref m);
    }

    private nint HeaderHandle => User32.SendMessage(Handle, User32.LvmGetHeader, 0, 0);

    private int GetTopIndex() => (int)User32.SendMessage(Handle, User32.LvmGetTopIndex, 0, 0);

    private void AttachImageList() => User32.SendMessage(Handle, User32.LvmSetImageList, User32.LvsilSmall, _sharedImageList);

    private void RaiseHeaderContextMenu(nint header, nint lParam)
    {
        var column = -1;
        var point = new User32.NativePoint();
        if (unchecked((int)lParam) == -1)
        {
            // From the keyboard: no cursor position, so no column; anchor the menu at the header.
            User32.ClientToScreen(header, ref point);
        }
        else
        {
            // Signed words: screen coordinates are negative left of or above the primary monitor.
            point.X = unchecked((short)lParam);
            point.Y = unchecked((short)(lParam >> 16));
            var hit = new User32.HdHitTestInfo { Point = point };
            User32.ScreenToClient(header, ref hit.Point);
            column = (int)User32.SendMessage(header, User32.HdmHitTest, 0, ref hit);
        }

        HeaderContextMenu?.Invoke(column, new Point(point.X, point.Y));
    }
}
