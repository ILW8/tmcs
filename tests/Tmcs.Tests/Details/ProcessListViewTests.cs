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
    private const uint WmNotify = 0x004E;
    private const int LvnGetDispInfoW = -177;
    private const int NmCustomDraw = -12;
    private const uint LvifText = 0x1;
    private const uint LvifState = 0x8;
    private const uint LvifIndent = 0x10;
    private const int LvnOdCacheHint = -113;
    private const uint CddsPrePaint = 0x1;
    private const uint CddsItemPrePaint = 0x10001;
    private const nint CdrfNewFont = 0x2;
    private const nint CdrfNotifyItemDraw = 0x20;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NmHdr
    {
        public nint HwndFrom;
        public nuint IdFrom;
        public int Code;
    }

    /// <summary><c>NMLVDISPINFOW</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NmLvDispInfo
    {
        public NmHdr Hdr;
        public LvItem Item;
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

    // The handler reads the notification through these, so a field in the wrong place would read or write the wrong memory.
    [Fact]
    public unsafe void Notify_structs_have_the_x64_layout()
    {
        Assert.Equal(24, sizeof(User32.NmHdr));
        Assert.Equal(16, (int)Marshal.OffsetOf<User32.NmHdr>(nameof(User32.NmHdr.Code)));
        Assert.Equal(88, sizeof(User32.LvItem));
        Assert.Equal(24, (int)Marshal.OffsetOf<User32.LvItem>(nameof(User32.LvItem.Text)));
        Assert.Equal(32, (int)Marshal.OffsetOf<User32.LvItem>(nameof(User32.LvItem.TextMax)));
        Assert.Equal(36, (int)Marshal.OffsetOf<User32.LvItem>(nameof(User32.LvItem.Image)));
        Assert.Equal(48, (int)Marshal.OffsetOf<User32.LvItem>(nameof(User32.LvItem.Indent)));
        Assert.Equal(64, (int)Marshal.OffsetOf<User32.LvItem>(nameof(User32.LvItem.ColumnIndexes)));
        Assert.Equal(24 + 88, sizeof(User32.NmLvDispInfo));
        Assert.Equal(24, (int)Marshal.OffsetOf<User32.NmLvDispInfo>(nameof(User32.NmLvDispInfo.Item)));
        Assert.Equal(80, sizeof(User32.NmCustomDraw));
        Assert.Equal(24, (int)Marshal.OffsetOf<User32.NmCustomDraw>(nameof(User32.NmCustomDraw.DrawStage)));
        Assert.Equal(56, (int)Marshal.OffsetOf<User32.NmCustomDraw>(nameof(User32.NmCustomDraw.ItemSpec)));
        Assert.Equal(136, sizeof(User32.NmLvCustomDraw));
        Assert.Equal(80, (int)Marshal.OffsetOf<User32.NmLvCustomDraw>(nameof(User32.NmLvCustomDraw.ClrText)));
        Assert.Equal(84, (int)Marshal.OffsetOf<User32.NmLvCustomDraw>(nameof(User32.NmLvCustomDraw.ClrTextBk)));
    }

    [Fact]
    public void Cell_source_answers_text_and_image_requests_instead_of_RetrieveVirtualItem() => Sta.Run(() =>
    {
        var requests = 0;
        using var form = Host(out var list, row: i =>
        {
            requests++;
            return new ListViewItem(Cells(i)) { ImageIndex = 1 };
        });
        var cells = new FakeCells();
        list.CellSource = cells;
        form.ShowOffscreen();

        Assert.Equal("3:2", ItemText(list, 3, 2, 64));
        Assert.Equal("4:0", ItemText(list, 4, 0, 64));
        Assert.Equal(13, ItemImage(list, 3));
        Assert.Equal(0, requests);
        Assert.True(cells.Served >= 2, $"the cell source served {cells.Served} cells");

        list.CellSource = null; // WinForms' own handling again
        Assert.Equal("a", ItemText(list, 3, 1, 64));
        Assert.Equal(1, ItemImage(list, 3));
        Assert.True(requests > 0, "without a cell source the list did not ask RetrieveVirtualItem");
    });

    [Fact]
    public unsafe void Cell_text_is_cut_to_cchTextMax_and_nothing_past_it_is_written() => Sta.Run(() =>
    {
        using var form = Host(out var list);
        list.CellSource = new FakeCells("0123456789");
        form.ShowOffscreen();

        foreach (var max in new[] { int.MinValue, -1, 0, 1, 2, 10, 11, 12, 64 })
        {
            var buffer = new char[80];
            Array.Fill(buffer, '￿');
            fixed (char* p = buffer) DispInfo(form, list, new LvItem { Mask = LvifText, Item = 1, SubItem = 2, Text = (nint)p, TextMax = max });

            var written = max <= 0 ? 0 : Math.Min(10, max - 1);
            Assert.Equal("0123456789"[..written], new string(buffer, 0, written));
            var untouched = written;
            if (max > 0) Assert.Equal('\0', buffer[untouched++]);
            Assert.All(buffer[untouched..], c => Assert.Equal('￿', c));
        }

        DispInfo(form, list, new LvItem { Mask = LvifText, Item = 1, SubItem = 2, Text = 0, TextMax = 10 }); // no buffer: nothing to write
    });

    [Fact]
    public void Cell_source_sets_the_first_columns_image_and_the_indent_and_leaves_the_rest_alone() => Sta.Run(() =>
    {
        using var form = Host(out var list);
        list.CellSource = new FakeCells();
        form.ShowOffscreen();

        var first = DispInfo(form, list, new LvItem { Mask = LvifImage | LvifIndent | LvifState, Item = 3, State = 0x1234, StateMask = 0xF000, Image = -7, Indent = 5, Param = 99 });
        Assert.Equal(13, first.Image);
        Assert.Equal(0, first.Indent);
        Assert.Equal(0x1234u, first.State);
        Assert.Equal(0xF000u, first.StateMask);
        Assert.Equal(99, first.Param);

        Assert.Equal(-7, DispInfo(form, list, new LvItem { Mask = LvifImage, Item = 3, SubItem = 1, Image = -7 }).Image); // sub-items have no image
        var notAsked = DispInfo(form, list, new LvItem { Mask = LvifState, Item = 3, Image = -7, Indent = 5 });
        Assert.Equal(-7, notAsked.Image);
        Assert.Equal(5, notAsked.Indent);
    });

    // WinForms gives every item the list's text colors at CDDS_ITEMPREPAINT, which also keeps the hot item's text from turning
    // hot-track blue, but it reads Items[i] for them, a RetrieveVirtualItem per row. With a cell source the list gives the same
    // answers without reading the item.
    [Fact]
    public void Custom_draw_gives_each_row_the_list_colors_without_RetrieveVirtualItem() => Sta.Run(() =>
    {
        var requests = 0;
        using var form = Host(out var list, row: i =>
        {
            // The test resets the count once the form has shown.
            // ReSharper disable once AccessToModifiedClosure
            requests++;
            return new ListViewItem(Cells(i));
        });
        list.ForeColor = Color.FromArgb(0x10, 0x20, 0x30);
        list.BackColor = Color.FromArgb(0xF0, 0xE0, 0xD0);
        list.CellSource = new FakeCells();
        form.ShowOffscreen();
        requests = 0;

        Assert.Equal(CdrfNotifyItemDraw | CdrfNewFont, CustomDraw(form, list, CddsPrePaint, item: 0).Result); // as WinForms answers
        var item = CustomDraw(form, list, CddsItemPrePaint, item: 1);
        Assert.Equal(CdrfNewFont, item.Result);
        Assert.Equal(0x302010, item.ClrText); // COLORREF: 0x00BBGGRR
        Assert.Equal(0xD0E0F0, item.ClrTextBk);
        Assert.Equal(0, requests);

        list.CellSource = null; // WinForms' own custom draw, which reads the item
        Assert.Equal(CdrfNotifyItemDraw | CdrfNewFont, CustomDraw(form, list, CddsPrePaint, item: 0).Result);
        var winForms = CustomDraw(form, list, CddsItemPrePaint, item: 1);
        Assert.True(requests > 0, "without a cell source WinForms did not read the item");
        Assert.Equal((item.ClrText, item.ClrTextBk), (winForms.ClrText, winForms.ClrTextBk));
    });

    // WinForms raises CacheVirtualItems for every LVN_ODCACHEHINT, which comes at the start of every paint, with a new event args.
    // With a cell source there is nothing to cache.
    [Fact]
    public unsafe void Cache_hint_is_answered_without_CacheVirtualItems_with_a_cell_source() => Sta.Run(() =>
    {
        using var form = Host(out var list);
        var raised = 0;
        // The test resets the count once the form has shown.
        // ReSharper disable once AccessToModifiedClosure
        list.CacheVirtualItems += (_, _) => raised++;
        list.CellSource = new FakeCells();
        form.ShowOffscreen();
        raised = 0;

        var hint = stackalloc int[8]; // NMLVCACHEHINT: NMHDR, then iFrom and iTo
        var header = (NmHdr*)hint;
        header->HwndFrom = list.Handle;
        header->Code = LvnOdCacheHint;
        hint[6] = 0;
        hint[7] = 3;
        User32.SendMessage(form.Handle, WmNotify, 0, (nint)hint);
        Assert.Equal(0, raised);

        list.CellSource = null;
        User32.SendMessage(form.Handle, WmNotify, 0, (nint)hint);
        Assert.Equal(1, raised);
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

    /// <summary>Cell <c>item:subItem</c>, or <paramref name="text"/> for every cell, cut to fit; image <c>item + 10</c>. Counts the cells it writes.</summary>
    private sealed class FakeCells(string? text = null) : ICellSource
    {
        public int Served { get; private set; }

        public bool TryWriteCell(int item, int subItem, Span<char> destination, out int written)
        {
            Served++;
            var cell = text ?? $"{item}:{subItem}";
            written = Math.Min(cell.Length, destination.Length);
            cell.AsSpan(0, written).CopyTo(destination);
            return written == cell.Length;
        }

        public int ImageIndex(int item) => item + 10;
    }

    /// <summary>Sends <c>LVN_GETDISPINFOW</c> the way the list does, through its parent, which reflects it back; returns the item as answered.</summary>
    private static unsafe LvItem DispInfo(Form parent, ProcessListView list, LvItem item)
    {
        var info = new NmLvDispInfo { Hdr = new NmHdr { HwndFrom = list.Handle, Code = LvnGetDispInfoW }, Item = item };
        User32.SendMessage(parent.Handle, WmNotify, 0, (nint)(&info));
        return info.Item;
    }

    /// <summary>
    /// Sends <c>NM_CUSTOMDRAW</c> through the parent, as the list does, in a zeroed <c>NMLVCUSTOMDRAW</c> whose text colors start
    /// as <c>0x123456</c>; returns the <c>CDRF_*</c> answer and the text colors as answered.
    /// </summary>
    private static unsafe (nint Result, int ClrText, int ClrTextBk) CustomDraw(Form parent, ProcessListView list, uint stage, int item)
    {
        var block = stackalloc byte[256]; // NMLVCUSTOMDRAW is 136 bytes on x64
        var header = (NmHdr*)block;
        header->HwndFrom = list.Handle;
        header->Code = NmCustomDraw;
        *(uint*)(block + 24) = stage; // NMCUSTOMDRAW.dwDrawStage
        *(nuint*)(block + 56) = (nuint)item; // NMCUSTOMDRAW.dwItemSpec
        *(int*)(block + 80) = *(int*)(block + 84) = 0x123456; // clrText, clrTextBk
        var result = User32.SendMessage(parent.Handle, WmNotify, 0, (nint)block);
        return (result, *(int*)(block + 80), *(int*)(block + 84));
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
