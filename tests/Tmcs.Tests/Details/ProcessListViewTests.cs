using System.Reflection;
using System.Runtime.InteropServices;
using Tmcs.Details;
using Tmcs.Icons;
using Tmcs.Native;

namespace Tmcs.Tests.Details;

public partial class ProcessListViewTests
{
    // Values the tests check are written out here rather than taken from User32, so a wrong production constant shows up.
    private const uint LvmGetImageList = 0x1002;
    private const uint LvmGetHeader = 0x101F;
    private const uint LvmGetTopIndex = 0x1027;
    private const uint LvmGetCountPerPage = 0x1028;
    private const uint LvmGetItemW = 0x104B;
    private const uint LvmGetItemTextW = 0x1073;
    private const uint LvmScroll = 0x1014;
    private const uint LvmSetItemState = 0x102B;
    private const uint LvmSetItemCount = 0x102F;
    private const uint LvisFocused = 0x1;
    private const uint LvisSelected = 0x2;
    private const uint HdmGetItemRect = 0x1207;
    private const uint HdmGetItemW = 0x120B;
    private const uint WmContextMenu = 0x007B;
    private const uint LvifImage = 0x2;
    private const uint HdiFormat = 0x4;
    private const int HdfRight = 0x1;
    private const int HdfSortDown = 0x200;
    private const int HdfSortUp = 0x400;
    private const int LvsilSmall = 1;
    private const int GwlStyle = -16;
    private const int LvsShareImageLists = 0x40;

    /// <summary><c>LVITEMW</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LvItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public nint Text;
        public int TextMax;
        public int Image;
        public nint Param;
        public int Indent;
        public int GroupId;
        public uint Columns;
        public nint ColumnIndexes;
        public nint ColumnFormats;
        public int Group;
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, uint msg, nint wParam, ref LvItem lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [Fact]
    public void Virtual_item_image_index_reaches_native_control() => Sta.Run(() =>
    {
        var icon = ShellIconLoader.LoadGenericIcon();
        Assert.NotEqual(0, icon);
        try
        {
            using var images = new NativeImageList(16);
            for (var i = 0; i < 3; i++) images.Add(icon);
            Assert.Equal(3, images.Count);

            using (var form = Host(out var list, row: i => new ListViewItem(Cells(i)) { ImageIndex = 2 }))
            {
                form.ShowOffscreen();
                list.SharedImageList = images.Handle; // the handle exists, so the setter attaches the list
                Assert.Equal(LvsShareImageLists, StyleBits(list) & LvsShareImageLists);
                Assert.Equal(images.Handle, SmallImageList(list));
                Assert.Equal(2, ItemImage(list, 1));

                var before = list.Handle;
                RecreateHandle(list); // destroys the old list view window, which must leave the image list alone
                Assert.NotEqual(before, list.Handle);
                Assert.Equal(LvsShareImageLists, StyleBits(list) & LvsShareImageLists);
                Assert.Equal(images.Handle, SmallImageList(list)); // re-attached by OnHandleCreated
                Assert.Equal(3, images.Count);
                Assert.Equal(2, ItemImage(list, 1));
            }

            Assert.Equal(3, images.Count); // still alive after the last list view window is gone
        }
        finally { User32.DestroyIcon(icon); }
    });

    [Fact]
    public void Sort_arrow_sets_header_format_flags() => Sta.Run(() =>
    {
        using var form = Host(out var list);
        list.Columns[3].TextAlign = HorizontalAlignment.Right;
        form.ShowOffscreen();
        var widthChanges = 0;
        list.ColumnWidthChanged += (_, _) => widthChanges++;
        var plain3 = HeaderFormat(list, 3);
        var plain4 = HeaderFormat(list, 4);
        Assert.Equal(HdfRight, plain3 & HdfRight);

        list.SetSortArrow(4, true);
        var fmt4 = HeaderFormat(list, 4);
        Assert.Equal(HdfSortDown, fmt4 & HdfSortDown);
        Assert.Equal(0, fmt4 & HdfSortUp);
        Assert.Equal(0, HeaderFormat(list, 0) & (HdfSortUp | HdfSortDown));

        list.SetSortArrow(3, false); // the arrow moves; the other format bits, such as alignment, stay
        Assert.Equal(plain3 | HdfSortUp, HeaderFormat(list, 3));
        Assert.Equal(plain4, HeaderFormat(list, 4));

        list.SetSortArrow(-1, false);
        for (var c = 0; c < 5; c++) Assert.Equal(0, HeaderFormat(list, c) & (HdfSortUp | HdfSortDown));
        Assert.Equal(plain3, HeaderFormat(list, 3));

        Assert.Equal(0, widthChanges); // a format-only header change is not a column resize
    });

    // WinForms' VirtualListSize setter restores the top row itself, but skips that when the handler returns the same
    // ListViewItem object for every row (it compares references), so both kinds of handler are covered.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Item_count_change_keeps_top_index(bool reuseOneItem) => Sta.Run(() =>
    {
        using var form = Host(out var list, rows: 500, row: RowSource(reuseOneItem));
        form.ShowOffscreen();
        list.EnsureVisible(150);
        var top = TopIndex(list);
        Assert.True(top > 0, $"top index {top}: the list did not scroll");

        list.SetItemCountKeepScroll(520);

        Assert.Equal(520, list.VirtualListSize);
        Assert.Equal(top, TopIndex(list));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Item_count_drop_keeps_top_index_until_too_few_rows_remain_below_it(bool reuseOneItem) => Sta.Run(() =>
    {
        using var form = Host(out var list, rows: 500, row: RowSource(reuseOneItem));
        form.ShowOffscreen();
        list.EnsureVisible(150);
        var top = TopIndex(list);
        var perPage = (int)User32.SendMessage(list.Handle, LvmGetCountPerPage, 0, 0);
        Assert.True(top >= perPage, $"top index {top}, {perPage} rows per page");

        list.SetItemCountKeepScroll(top + perPage + 1); // rows removed below the page only
        Assert.Equal(top, TopIndex(list));

        list.SetItemCountKeepScroll(top + 3); // the old top row can no longer start a full page: show the last page
        Assert.Equal(top + 3 - perPage, TopIndex(list));
    });

    // On a count change the native control scrolls while it applies the count, to the focused row or to row 0 when none is
    // focused, unless it is told not to. Putting the top row back afterwards ends at the same place, but each scroll is
    // drawn at once, which shows as a flicker. So the top index is read inside the call, right after the message.
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Item_count_change_never_moves_the_top_index_not_even_inside_the_call(bool focusOffscreenRow, bool reuseOneItem) => Sta.Run(() =>
    {
        using var form = Host(out var list, rows: 500, row: RowSource(reuseOneItem));
        form.ShowOffscreen();
        if (focusOffscreenRow)
        {
            var state = new LvItem { State = LvisFocused | LvisSelected, StateMask = LvisFocused | LvisSelected };
            SendMessage(list.Handle, LvmSetItemState, 5, ref state);
        }

        list.EnsureVisible(150);
        var top = TopIndex(list);
        Assert.True(top > 5, $"top index {top}: the focused row 5 is not above the view");

        var spy = new TopIndexSpy(list);
        try { list.SetItemCountKeepScroll(520); }
        finally { spy.ReleaseHandle(); } // before the form, and with it the window, goes

        Assert.Equal(520, list.VirtualListSize);
        Assert.Contains(spy.Seen, s => s.Message == LvmSetItemCount); // the spy is on the window, or the check below proves nothing
        var moved = spy.Seen.Where(s => s.Top != top).Select(s => $"0x{s.Message:X} left the top at {s.Top}").ToList();
        Assert.True(moved.Count == 0, $"top index {top} moved inside the call: {string.Join("; ", moved)}");
        Assert.Equal(top, TopIndex(list));
    });

    [Fact]
    public void Very_long_text_is_served_without_error() => Sta.Run(() =>
    {
        var longText = string.Create(40_000, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = (char)('a' + i % 26);
        });
        var requests = 0;
        using var form = Host(out var list, row: i =>
        {
            requests++;
            return new ListViewItem(["r" + i, longText, "b", "c", "d"]);
        });
        form.ShowOffscreen();

        var before = requests;
        var text = ItemText(list, 0, 1, 1024);

        Assert.True(requests > before, "LvmGetItemTextW did not ask RetrieveVirtualItem for the text");
        Assert.Equal(1023, text.Length);
        Assert.Equal(longText[..text.Length], text);
    });

    [Fact]
    public void Header_right_click_raises_HeaderContextMenu_with_column() => Sta.Run(() =>
    {
        using var form = Host(out var list);
        using var rowMenu = new ContextMenuStrip();
        var rowMenuOpening = false;
        rowMenu.Opening += (_, e) =>
        {
            rowMenuOpening = true;
            e.Cancel = true; // never shown
        };
        list.ContextMenuStrip = rowMenu;
        var raised = new List<(int Column, Point Screen)>();
        list.HeaderContextMenu += (column, screen) => raised.Add((column, screen));
        form.ShowOffscreen();

        var header = Header(list);
        var rect = new User32.NativeRect();
        Assert.NotEqual(0, User32.SendMessage(header, HdmGetItemRect, 2, ref rect));
        var point = new User32.NativePoint { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 };
        Assert.True(User32.ClientToScreen(header, ref point));

        // Sent to the header, where a right-click lands; DefWindowProc passes it to the list view with wParam = header.
        User32.SendMessage(header, WmContextMenu, header, MakeLParam(point.X, point.Y));

        Assert.Equal([(2, new Point(point.X, point.Y))], raised);
        Assert.False(rowMenuOpening, "the header right-click also opened the row context menu");
    });

    [Fact]
    public void Keyboard_context_menu_on_header_reports_no_column_at_the_header() => Sta.Run(() =>
    {
        using var form = Host(out var list);
        var raised = new List<(int Column, Point Screen)>();
        list.HeaderContextMenu += (column, screen) => raised.Add((column, screen));
        form.ShowOffscreen();

        var header = Header(list);
        var origin = new User32.NativePoint();
        Assert.True(User32.ClientToScreen(header, ref origin));

        User32.SendMessage(header, WmContextMenu, header, -1);

        Assert.Equal([(-1, new Point(origin.X, origin.Y))], raised);
    });

    /// <summary>
    /// Watches a list view's messages from the outside, so it sees them as sent, before the control's own WndProc. After each
    /// <c>LVM_SETITEMCOUNT</c> and <c>LVM_SCROLL</c> has been processed it records the top index. It changes nothing.
    /// </summary>
    private sealed class TopIndexSpy : NativeWindow
    {
        private readonly ProcessListView _list;

        public TopIndexSpy(ProcessListView list)
        {
            _list = list;
            AssignHandle(list.Handle);
        }

        /// <summary>The message and the top index right after it was processed, in order.</summary>
        public List<(uint Message, int Top)> Seen { get; } = [];

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg is (int)LvmSetItemCount or (int)LvmScroll) Seen.Add(((uint)m.Msg, TopIndex(_list)));
        }
    }

    /// <summary>An 800×600 off-screen form filled by a list with five 100 px columns and <paramref name="rows"/> rows.</summary>
    private static OffscreenForm Host(out ProcessListView list, int rows = 5, Func<int, ListViewItem>? row = null)
    {
        var form = new OffscreenForm { ClientSize = new Size(800, 600) };
        list = new ProcessListView { Dock = DockStyle.Fill };
        for (var c = 0; c < 5; c++) list.Columns.Add("Column " + c, 100);
        row ??= i => new ListViewItem(Cells(i));
        list.RetrieveVirtualItem += (_, e) => e.Item = row(e.ItemIndex);
        list.VirtualListSize = rows;
        form.Controls.Add(list);
        return form;
    }

    private static string[] Cells(int row) => ["r" + row, "a", "b", "c", "d"];

    private static Func<int, ListViewItem> RowSource(bool reuseOneItem)
    {
        if (!reuseOneItem) return i => new ListViewItem(Cells(i));
        var item = new ListViewItem(Cells(0));
        return _ => item;
    }

    private static nint Header(ProcessListView list) => User32.SendMessage(list.Handle, LvmGetHeader, 0, 0);

    private static int TopIndex(ProcessListView list) => (int)User32.SendMessage(list.Handle, LvmGetTopIndex, 0, 0);

    private static nint SmallImageList(ProcessListView list) => User32.SendMessage(list.Handle, LvmGetImageList, LvsilSmall, 0);

    private static int StyleBits(ProcessListView list) => (int)GetWindowLongPtr(list.Handle, GwlStyle);

    private static void RecreateHandle(Control control) =>
        typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, null);

    /// <summary>Packs a screen point the way <c>MAKELPARAM</c> does.</summary>
    private static nint MakeLParam(int x, int y) => (nint)(uint)((y << 16) | (x & 0xFFFF));

    private static int HeaderFormat(ProcessListView list, int column)
    {
        var item = new User32.HdItem { Mask = HdiFormat };
        Assert.NotEqual(0, User32.SendMessage(Header(list), HdmGetItemW, column, ref item));
        return item.Format;
    }

    private static int ItemImage(ProcessListView list, int row)
    {
        var item = new LvItem { Mask = LvifImage, Item = row };
        Assert.NotEqual(0, SendMessage(list.Handle, LvmGetItemW, 0, ref item));
        return item.Image;
    }

    private static unsafe string ItemText(ProcessListView list, int row, int subItem, int capacity)
    {
        var buffer = new char[capacity];
        fixed (char* p = buffer)
        {
            var item = new LvItem { SubItem = subItem, Text = (nint)p, TextMax = capacity };
            var length = (int)SendMessage(list.Handle, LvmGetItemTextW, row, ref item);
            return new string(p, 0, length);
        }
    }
}
