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

    /// <summary><c>LVM_SETITEMCOUNT</c>: wParam is the new item count, lParam the <c>LVSICF_*</c> flags.</summary>
    private const int LvmSetItemCount = 0x102F;

    /// <summary>
    /// <c>LVSICF_NOSCROLL</c>: the control keeps the top row where it is while it applies the new count. Without it, an
    /// owner-data list view scrolls to the focused row, or to row 0 when none is focused, and WinForms always sends 0.
    /// </summary>
    private const int LvsicfNoScroll = 0x2;

    private nint _sharedImageList;

    /// <summary>True while <see cref="SetSortArrow"/> writes a header item, whose notification is not a column resize.</summary>
    private bool _writingSortArrow;

    /// <summary>The list's colors as <c>COLORREF</c>s, read once per paint at <c>CDDS_PREPAINT</c> and given to every row.</summary>
    private int _rowTextColor, _rowBackColor;

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
    /// A context menu was asked for on the rows: the screen point to show the menu at. From the keyboard, that is the focused row's top
    /// left, kept inside the list, or the list's top left when no row has the focus.
    /// </summary>
    public event Action<Point>? RowContextMenu;

    /// <summary>
    /// When set, the list answers the native control's requests for cell text and icons itself, from this source, and its custom
    /// draw and cache hints too: painting then never raises <see cref="ListView.RetrieveVirtualItem"/>, which builds a whole
    /// <see cref="ListViewItem"/> per cell, nor <see cref="ListView.CacheVirtualItems"/>. The WinForms paths that read whole items
    /// still raise <see cref="ListView.RetrieveVirtualItem"/>. When null, WinForms handles all of them as usual.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // runtime wiring, never designer state
    public ICellSource? CellSource { get; set; }

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
    /// remain below it to fill the page, in which case the last page is shown. <see cref="WndProc"/> adds <c>LVSICF_NOSCROLL</c>
    /// to every native count message, so the control never scrolls, not even briefly. Scrolling back afterwards would not do:
    /// both scrolls are drawn at once, which shows as a flicker.
    /// </summary>
    public void SetItemCountKeepScroll(int count) => VirtualListSize = count;

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

        // Base would open a ContextMenuStrip, or pass the message on to the parent; a right-click outside the client area goes to
        // base, which shows the scrollbar's own menu.
        if (m.Msg == WmContextMenu && m.WParam != 0 && m.WParam == Handle && RaiseRowContextMenu(m.LParam)) return;

        // The list's own notifications, which its parent reflects back here.
        if (m.Msg == User32.WmReflectNotify && m.LParam != 0 && CellSource is { } cells && AnswerNotify(ref m, cells)) return;

        // WinForms sends this from its VirtualListSize setter, and with flags 0. Every count change goes through here.
        if (m.Msg == LvmSetItemCount) m.LParam |= LvsicfNoScroll;

        base.WndProc(ref m);
    }

    /// <summary>
    /// Answers the notifications that drawing rows sends, so that WinForms, which would raise <see cref="ListView.RetrieveVirtualItem"/>
    /// or <see cref="ListView.CacheVirtualItems"/> for them, never sees them. Returns false for any other, which WinForms handles.
    /// </summary>
    private unsafe bool AnswerNotify(ref Message m, ICellSource cells)
    {
        var header = (User32.NmHdr*)m.LParam;
        switch (header->Code)
        {
            case User32.LvnGetDispInfoW:
                AnswerDispInfo(ref ((User32.NmLvDispInfo*)header)->Item, cells);
                return true;
            case User32.LvnOdCacheHint:
                // The cells come straight from the source, so there is nothing to cache, and WinForms would build event args for it.
                return true;
            case User32.NmCustomDrawCode:
                return AnswerCustomDraw(ref m, ref *(User32.NmLvCustomDraw*)header);
            default:
                return false;
        }
    }

    /// <summary>
    /// Draws rows as WinForms' own custom draw does, without it reading <c>Items[i]</c> for each row, which raises
    /// <see cref="ListView.RetrieveVirtualItem"/>: the rows have no colors or font of their own, so each gets the list's colors, and
    /// the font the control already selected into the DC. The stages WinForms does not ask for, it gets to answer.
    /// </summary>
    private bool AnswerCustomDraw(ref Message m, ref User32.NmLvCustomDraw draw)
    {
        switch (draw.Nmcd.DrawStage)
        {
            case User32.CddsPrePaint:
                _rowTextColor = ColorTranslator.ToWin32(ForeColor);
                _rowBackColor = ColorTranslator.ToWin32(BackColor);
                m.Result = User32.CdrfNotifyItemDraw | User32.CdrfNewFont;
                return true;
            case User32.CddsItemPrePaint:
                draw.ClrText = _rowTextColor;
                draw.ClrTextBk = _rowBackColor;
                m.Result = User32.CdrfNewFont;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Fills in what <c>LVN_GETDISPINFOW</c> asks for: the text of one cell, the row's icon, and no indent. The other mask bits are left
    /// as the control set them, as WinForms leaves them.
    /// </summary>
    private static unsafe void AnswerDispInfo(ref User32.LvItem item, ICellSource cells)
    {
        if ((item.Mask & User32.LvifText) != 0 && item.Text != 0 && item.TextMax > 0)
        {
            // Straight into the control's buffer. The text gets at most TextMax - 1 characters, so the NUL always fits.
            var buffer = new Span<char>((void*)item.Text, item.TextMax);
            cells.TryWriteCell(item.Item, item.SubItem, buffer[..^1], out var written);
            buffer[Math.Clamp(written, 0, buffer.Length - 1)] = '\0';
        }

        if ((item.Mask & User32.LvifImage) != 0 && item.SubItem == 0)
        {
            var image = cells.ImageIndex(item.Item);
            if (image >= 0) item.Image = image; // -1 leaves the field alone, as WinForms does for an item without an image
        }

        if ((item.Mask & User32.LvifIndent) != 0) item.Indent = 0;
    }

    private nint HeaderHandle => User32.SendMessage(Handle, User32.LvmGetHeader, 0, 0);

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

    /// <returns>False for a right click outside the client area, on a scrollbar: it keeps its own menu.</returns>
    private bool RaiseRowContextMenu(nint lParam)
    {
        Point screen;
        if (unchecked((int)lParam) == -1)
        {
            // From the keyboard: no cursor position, so anchor the menu at the focused row, which may be scrolled out of view.
            var client = Point.Empty;
            var focus = (int)User32.SendMessage(Handle, User32.LvmGetNextItem, -1, User32.LvniFocused);
            var rect = new User32.NativeRect { Left = User32.LvirBounds };
            if (focus >= 0 && User32.SendMessage(Handle, User32.LvmGetItemRect, focus, ref rect) != 0)
                client = new Point(Math.Clamp(rect.Left, 0, ClientSize.Width), Math.Clamp(rect.Top, 0, ClientSize.Height));
            screen = PointToScreen(client);
        }
        else
        {
            // Signed words: screen coordinates are negative left of or above the primary monitor.
            screen = new Point(unchecked((short)lParam), unchecked((short)(lParam >> 16)));
            if (!ClientRectangle.Contains(PointToClient(screen))) return false;
        }

        RowContextMenu?.Invoke(screen);
        return true;
    }
}
