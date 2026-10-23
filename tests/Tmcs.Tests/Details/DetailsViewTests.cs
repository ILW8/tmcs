using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Tmcs.Details;
using Tmcs.Icons;
using Tmcs.Native;
using Tmcs.Processes;
using Tmcs.Settings;

namespace Tmcs.Tests.Details;

// No test opens the header menu or the Select columns dialog: the rig replaces DetailsView's TrackHeaderMenu and ChooseColumns
// with stand-ins, and the dialog test only constructs the dialog.
public partial class DetailsViewTests
{
    // Values the tests check are written out here rather than taken from User32, so a wrong production constant shows up.
    private const uint LvmGetHeader = 0x101F;
    private const uint LvmGetColumnOrderArray = 0x103B;
    private const uint LvmGetItemW = 0x104B;
    private const uint LvmFindItemW = 0x1053;
    private const uint LvmGetItemTextW = 0x1073;
    private const uint HdmGetItemRect = 0x1207;
    private const uint HdmGetItemW = 0x120B;
    private const uint WmNotify = 0x004E;
    private const uint WmContextMenu = 0x007B;
    private const int HdnEndDragW = -311;
    private const int LvnColumnClick = -108;
    private const int NmReleasedCapture = -16;
    private const uint HdiFormat = 0x4;
    private const uint HdiOrder = 0x80;
    private const int HdfSortDown = 0x200;
    private const int HdfSortUp = 0x400;
    private const uint LvifImage = 0x2;
    private const uint LvfiString = 0x2;
    private const uint LvfiPartial = 0x8;
    private const uint LvfiWrap = 0x20;
    private const uint MfByPosition = 0x400;
    private const uint MfGrayed = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct NmHdr
    {
        public nint HwndFrom;
        public nuint IdFrom;
        public int Code;
    }

    /// <summary><c>NMHEADERW</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NmHeader
    {
        public NmHdr Hdr;
        public int Item;
        public int Button;
        public User32.HdItem* Pitem;
    }

    /// <summary><c>NMLISTVIEW</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NmListView
    {
        public NmHdr Hdr;
        public int Item;
        public int SubItem;
        public uint NewState;
        public uint OldState;
        public uint Changed;
        public User32.NativePoint Action;
        public nint Param;
    }

    /// <summary><c>LVFINDINFOW</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct LvFindInfo
    {
        public uint Flags;
        public char* Text;
        public nint Param;
        public User32.NativePoint Point;
        public uint Direction;
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetMenuItemID(nint menu, int position);

    [LibraryImport("user32.dll")]
    private static partial uint GetMenuState(nint menu, uint item, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMenuStringW")]
    private static unsafe partial int GetMenuString(nint menu, uint item, char* buffer, int capacity, uint flags);

    [Fact]
    public void Shows_visible_columns_in_settings_order() => Sta.Run(() =>
    {
        using var rig = new Rig();

        Assert.Equal(DetailsColumns.All.Take(8).Select(c => c.Title), TitlesInDisplayOrder(rig.List));
        Assert.Equal(0, rig.SettingsChanged); // building the columns and creating the handle change nothing
    });

    [Fact]
    public void Column_widths_scale_from_96_dpi_and_alignment_follows_the_column() => Sta.Run(() =>
    {
        using var rig = new Rig();

        var dpi = rig.View.DeviceDpi;
        foreach (ColumnHeader header in rig.List.Columns)
        {
            var def = DetailsColumns.All.Single(d => d.Title == header.Text);
            Assert.Equal((int)Math.Round(def.DefaultWidth * dpi / 96.0, MidpointRounding.AwayFromZero), header.Width);
            if (header.Index > 0) Assert.Equal(def.Align, header.TextAlign); // the list forces the first column left
        }
        Assert.Equal(DetailsColumns.All.Select(d => d.DefaultWidth), rig.Settings.DetailsColumns.OrderBy(c => c.Id).Select(c => c.Width));
    });

    [Fact]
    public void Rows_render_formatted_values() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Row(2).IconSlot = 2;

        rig.View.RefreshRows();

        Assert.Equal(["alpha.exe", "mid.exe", "zeta.exe"], rig.View.DisplayedRows.Select(r => r.Name));
        Assert.Equal(3, rig.List.VirtualListSize);
        Assert.Equal("alpha.exe", ItemText(rig.List, 0, 0));
        Assert.Equal("2", ItemText(rig.List, 0, ColumnIndex(rig.List, "PID")));
        Assert.Equal("12,345 K", ItemText(rig.List, 0, ColumnIndex(rig.List, "Memory (active private working set)")));
        Assert.Equal("cmd 2", ItemText(rig.List, 0, ColumnIndex(rig.List, "Command line")));
        Assert.Equal(2, ItemImage(rig.List, 0));
    });

    [Fact]
    public void SortBy_same_column_toggles_direction() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Row(1).CpuPercent = 5;
        rig.Row(2).CpuPercent = 1;
        rig.Row(3).CpuPercent = 9;
        var cpu = ColumnIndex(rig.List, "CPU");

        rig.View.SortBy(DetailsColumn.Cpu);
        Assert.Equal(DetailsColumn.Cpu, rig.Settings.SortColumn);
        Assert.False(rig.Settings.SortDescending);
        Assert.Equal([2, 1, 3], Pids(rig.View));
        Assert.Equal(HdfSortUp, SortBits(rig.List, cpu));
        Assert.Equal(0, SortBits(rig.List, 0));

        rig.View.SortBy(DetailsColumn.Cpu);
        Assert.True(rig.Settings.SortDescending);
        Assert.Equal([3, 1, 2], Pids(rig.View));
        Assert.Equal(HdfSortDown, SortBits(rig.List, cpu));

        rig.View.SortBy(DetailsColumn.Pid); // a new column starts ascending
        Assert.Equal(DetailsColumn.Pid, rig.Settings.SortColumn);
        Assert.False(rig.Settings.SortDescending);
        Assert.Equal([1, 2, 3], Pids(rig.View));
        Assert.Equal(0, SortBits(rig.List, cpu));
        Assert.Equal(3, rig.SettingsChanged);
    });

    [Fact]
    public void Header_click_sorts_by_the_clicked_column() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.View.RefreshRows();
        var pid = ColumnIndex(rig.List, "PID");

        ClickHeader(rig, pid);
        Assert.Equal(DetailsColumn.Pid, rig.Settings.SortColumn);
        Assert.Equal([1, 2, 3], Pids(rig.View));

        ClickHeader(rig, pid);
        Assert.True(rig.Settings.SortDescending);
        Assert.Equal([3, 2, 1], Pids(rig.View));
        Assert.Equal(2, rig.SettingsChanged);
    });

    [Fact]
    public void Sort_arrow_returns_after_the_list_handle_is_recreated() => Sta.Run(() =>
    {
        var settings = AppSettings.CreateDefault();
        settings.SortColumn = DetailsColumn.Pid;
        settings.SortDescending = true;
        using var rig = new Rig(settings);
        var pid = ColumnIndex(rig.List, "PID");
        Assert.Equal(HdfSortDown, SortBits(rig.List, pid));

        RecreateHandle(rig.List); // the list inserts its header items again, without format bits
        Application.DoEvents();

        Assert.Equal(HdfSortDown, SortBits(rig.List, pid));
        Assert.Equal(0, SortBits(rig.List, 0));
        Assert.Equal(0, rig.SettingsChanged);
    });

    [Fact]
    public void HideColumn_updates_settings_and_header() => Sta.Run(() =>
    {
        using var rig = new Rig();

        rig.View.HideColumn(DetailsColumn.UacVirtualization);

        Assert.Equal(7, rig.List.Columns.Count);
        Assert.Equal(DetailsColumns.All.Take(7).Select(c => c.Title), TitlesInDisplayOrder(rig.List));
        Assert.False(rig.Settings.DetailsColumns.Single(c => c.Id == DetailsColumn.UacVirtualization).Visible);
        Assert.Equal(Enumerable.Range(0, 7), rig.Settings.DetailsColumns.Where(c => c.Visible).Select(c => c.DisplayIndex).Order());
        Assert.Equal(1, rig.SettingsChanged);

        rig.View.HideColumn(DetailsColumn.Name);
        rig.View.HideColumn(DetailsColumn.Threads); // already hidden

        Assert.Equal(7, rig.List.Columns.Count);
        Assert.True(rig.Settings.DetailsColumns.Single(c => c.Id == DetailsColumn.Name).Visible);
        Assert.Equal(1, rig.SettingsChanged);
    });

    [Fact]
    public void Hiding_the_sort_column_resets_sort_to_name_ascending() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Row(1).CpuPercent = 9;
        rig.View.SortBy(DetailsColumn.Cpu);
        rig.View.SortBy(DetailsColumn.Cpu);
        Assert.Equal(1, rig.View.DisplayedRows[0].Id.Pid);

        rig.View.HideColumn(DetailsColumn.Cpu);

        Assert.Equal(DetailsColumn.Name, rig.Settings.SortColumn);
        Assert.False(rig.Settings.SortDescending);
        Assert.Equal(["alpha.exe", "mid.exe", "zeta.exe"], rig.View.DisplayedRows.Select(r => r.Name));
        Assert.Equal(HdfSortUp, SortBits(rig.List, 0));
        Assert.Equal(3, rig.SettingsChanged);
    });

    [Fact]
    public void Header_menu_offers_hide_only_for_a_hidable_column_under_the_cursor() => Sta.Run(() =>
    {
        using var rig = new Rig();
        var shown = new List<(string[] Items, bool HideEnabled, Point Screen)>();
        var chooseHide = new Queue<bool>([true, true, false, true]); // otherwise dismiss the menu
        rig.View.TrackHeaderMenu = (menu, screen) =>
        {
            var handle = menu.Handle;
            shown.Add(([MenuText(handle, 0), MenuText(handle, 1)], (GetMenuState(handle, 0, MfByPosition) & MfGrayed) == 0, screen));
            return chooseHide.Dequeue() ? (int)GetMenuItemID(handle, 0) : 0; // a real menu never returns a grayed item; the view must cope anyway
        };

        var nameScreen = RightClickHeader(rig.List, ColumnIndex(rig.List, "Name"));
        Assert.Equal(8, rig.List.Columns.Count);

        RightClickHeaderFromKeyboard(rig.List);
        Assert.Equal(8, rig.List.Columns.Count);

        RightClickHeader(rig.List, ColumnIndex(rig.List, "Status"));
        Assert.Equal(8, rig.List.Columns.Count);
        Assert.Equal(0, rig.SettingsChanged);

        RightClickHeader(rig.List, ColumnIndex(rig.List, "Status"));

        Assert.Equal(4, shown.Count);
        Assert.All(shown, s => Assert.Equal(["Hide column", "Select columns"], s.Items));
        Assert.Equal([false, false, true, true], shown.Select(s => s.HideEnabled));
        Assert.Equal(nameScreen, shown[0].Screen);
        Assert.DoesNotContain("Status", TitlesInDisplayOrder(rig.List));
        Assert.False(rig.Settings.DetailsColumns.Single(c => c.Id == DetailsColumn.Status).Visible);
        Assert.Equal(1, rig.SettingsChanged);
    });

    [Fact]
    public void Select_columns_from_header_menu_applies_the_chosen_set() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.View.TrackHeaderMenu = (menu, _) => (int)GetMenuItemID(menu.Handle, 1);
        var chosen = new HashSet<DetailsColumn> { DetailsColumn.CommitSize, DetailsColumn.Pid, DetailsColumn.Threads }; // Name is always kept
        var answers = new Queue<IReadOnlySet<DetailsColumn>?>([null, chosen, chosen]); // Cancel, then OK twice
        var asked = new List<AppSettings>();
        rig.View.ChooseColumns = s =>
        {
            asked.Add(s);
            return answers.Dequeue();
        };

        RightClickHeader(rig.List, 0);
        Assert.Same(rig.Settings, Assert.Single(asked));
        Assert.Equal(8, rig.List.Columns.Count);
        Assert.Equal(0, rig.SettingsChanged);

        RightClickHeader(rig.List, 0);

        Assert.Equal(["Name", "PID", "Threads", "Commit size"], TitlesInDisplayOrder(rig.List));
        Assert.Equal([DetailsColumn.Name, DetailsColumn.Pid, DetailsColumn.Threads, DetailsColumn.CommitSize], AppSettings.VisibleInDisplayOrder(rig.Settings));
        Assert.Equal(Enumerable.Range(0, 11), rig.Settings.DetailsColumns.Select(c => c.DisplayIndex));
        Assert.Equal(1, rig.SettingsChanged);

        RightClickHeader(rig.List, 0); // OK with the same set changes nothing
        Assert.Equal(1, rig.SettingsChanged);
    });

    [Fact]
    public void User_reorder_saves_display_order_and_name_keeps_the_icon_column() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.View.RefreshRows();

        EndDrag(rig.List, column: 0, toDisplayIndex: 2); // Name moves right
        Assert.Equal(1, rig.SettingsChanged);
        Assert.Equal(["PID", "Status", "Name", "User name", "CPU"], TitlesInDisplayOrder(rig.List).Take(5));
        Assert.Equal([DetailsColumn.Pid, DetailsColumn.Status, DetailsColumn.Name, DetailsColumn.UserName, DetailsColumn.Cpu], AppSettings.VisibleInDisplayOrder(rig.Settings).Take(5));

        EndDrag(rig.List, column: ColumnIndex(rig.List, "CPU"), toDisplayIndex: 1); // CPU moves left, past columns already moved

        string[] expected = ["PID", "CPU", "Status", "Name", "User name", "Memory (active private working set)", "Command line", "UAC virtualization"];
        Assert.Equal(expected, TitlesInDisplayOrder(rig.List));
        Assert.Equal(expected, AppSettings.VisibleInDisplayOrder(rig.Settings).Select(id => DetailsColumns.Get(id).Title));
        Assert.Equal(Enumerable.Range(0, 11), rig.Settings.DetailsColumns.Select(c => c.DisplayIndex).Order());
        Assert.Equal(2, rig.SettingsChanged);

        rig.View.ApplyColumnSettings(); // a rebuild from the saved layout gives the same picture

        Assert.Equal(expected, TitlesInDisplayOrder(rig.List));
        Assert.Equal("Name", rig.List.Columns[0].Text); // the first column carries the icon
        Assert.Equal("alpha.exe", ItemText(rig.List, 0, 0));
        Assert.Equal(2, rig.SettingsChanged);
    });

    [Fact]
    public void User_width_change_saves_96_dpi_units_and_rebuilds_stay_quiet() => Sta.Run(() =>
    {
        using var rig = new Rig();
        var dpi = rig.View.DeviceDpi;
        var pid = ColumnIndex(rig.List, "PID");

        rig.List.Columns[pid].Width = 70 * dpi / 96;

        Assert.Equal(70, rig.Settings.DetailsColumns.Single(c => c.Id == DetailsColumn.Pid).Width);
        Assert.Equal(1, rig.SettingsChanged);

        rig.View.ApplyColumnSettings();

        Assert.Equal(70 * dpi / 96, rig.List.Columns[ColumnIndex(rig.List, "PID")].Width);
        Assert.Equal(70, rig.Settings.DetailsColumns.Single(c => c.Id == DetailsColumn.Pid).Width);
        Assert.Equal(1, rig.SettingsChanged);
    });

    [Fact]
    public void Selection_follows_process_across_refresh() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Row(2).CpuPercent = 1;
        rig.Row(1).CpuPercent = 2;
        rig.Row(3).CpuPercent = 3;
        rig.View.SortBy(DetailsColumn.Cpu);
        Assert.Equal(2, rig.View.DisplayedRows[0].Id.Pid);
        rig.List.SelectedIndices.Add(0);
        rig.List.Items[0].Focused = true;

        rig.Row(2).CpuPercent = 3;
        rig.Row(3).CpuPercent = 1;
        rig.View.RefreshRows();

        Assert.Equal([3, 1, 2], Pids(rig.View));
        var selected = Assert.Single(rig.List.SelectedIndices.Cast<int>());
        Assert.Equal(2, rig.View.DisplayedRows[selected].Id.Pid);
        Assert.Equal(selected, rig.List.FocusedItem?.Index);
    });

    [Fact]
    public void Unchanged_selection_is_left_alone_on_refresh() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.View.RefreshRows();
        rig.List.SelectedIndices.Add(1);
        var changes = 0;
        rig.List.SelectedIndexChanged += (_, _) => changes++;

        rig.View.RefreshRows();
        rig.View.RefreshRows();

        Assert.Equal(0, changes);
        Assert.Equal([1], rig.List.SelectedIndices.Cast<int>());
    });

    [Fact]
    public void Selection_of_an_exited_process_clears() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.View.RefreshRows();
        rig.List.SelectedIndices.Add(0); // alpha.exe, PID 2

        rig.Snapshot.RemoveAll(p => p.Pid == 2);
        rig.Table.Apply(rig.Snapshot, 20_000_000);
        rig.View.RefreshRows();

        Assert.Equal(["mid.exe", "zeta.exe"], rig.View.DisplayedRows.Select(r => r.Name));
        Assert.Empty(rig.List.SelectedIndices.Cast<int>());
    });

    [Fact]
    public void Type_to_jump_finds_rows_by_name_prefix() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Snapshot.Add(new RawProcess(4, 0, 104, "mspaint.exe", 0, 0, 0, 0, 1, 1, 1, 8, false));
        rig.Table.Apply(rig.Snapshot, 20_000_000);
        rig.View.RefreshRows(); // alpha.exe, mid.exe, mspaint.exe, zeta.exe

        Assert.Equal(1, Find(rig.List, "M", -1));
        Assert.Equal(2, Find(rig.List, "m", 1)); // the list starts after the given row: typing again moves on to the next match
        Assert.Equal(2, Find(rig.List, "MS", -1));
        Assert.Equal(1, Find(rig.List, "m", 2)); // and wraps past the end
        Assert.Equal(-1, Find(rig.List, "q", -1));
    });

    [Fact]
    public void Select_columns_dialog_lists_11_columns_with_name_locked() => Sta.Run(() =>
    {
        using var dialog = new SelectColumnsDialog(AppSettings.CreateDefault());
        var list = dialog.Controls.OfType<CheckedListBox>().Single();

        Assert.Equal(11, list.Items.Count);
        Assert.Equal(DetailsColumns.All.Select(c => c.Title), list.Items.Cast<string>());
        Assert.Equal("Name", list.Items[0]);
        Assert.True(list.GetItemChecked(0));
        Assert.True(dialog.Selected.SetEquals(DetailsColumns.All.Take(8).Select(c => c.Id)));

        list.SetItemChecked(0, false);
        list.SetItemChecked((int)DetailsColumn.Pid, false);
        list.SetItemChecked((int)DetailsColumn.Threads, true);

        Assert.True(list.GetItemChecked(0));
        Assert.True(dialog.Selected.SetEquals(
            [DetailsColumn.Name, DetailsColumn.Status, DetailsColumn.UserName, DetailsColumn.Cpu, DetailsColumn.ActivePrivateWorkingSet, DetailsColumn.CommandLine, DetailsColumn.UacVirtualization, DetailsColumn.Threads]));
        Assert.False(dialog.Visible);
    });

    /// <summary>A details view over three processes, hosted in an off-screen form that is already shown.</summary>
    private sealed class Rig : IDisposable
    {
        public readonly List<RawProcess> Snapshot = [P(1, "zeta.exe"), P(2, "alpha.exe", privateWorkingSet: 12_641_280), P(3, "mid.exe")];
        public readonly ProcessTable Table = new(new FakeInfo(), 1);
        public readonly AppSettings Settings;
        public readonly DetailsView View;
        public int SettingsChanged;
        private readonly NativeImageList _images = new(16);
        private readonly OffscreenForm _form = new() { ClientSize = new Size(1200, 600) };

        public Rig(AppSettings? settings = null)
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US"); // this STA thread's own culture
            Settings = settings ?? AppSettings.CreateDefault();
            Table.Apply(Snapshot, 10_000_000);
            View = new DetailsView(Table, _images, Settings) { Dock = DockStyle.Fill };
            View.TrackHeaderMenu = (_, _) => throw new InvalidOperationException("the test would have shown the header menu");
            View.ChooseColumns = _ => throw new InvalidOperationException("the test would have shown the dialog");
            View.SettingsChanged += (_, _) => SettingsChanged++;
            _form.Controls.Add(View);
            _form.ShowOffscreen();
        }

        public ProcessListView List => View.Controls.OfType<ProcessListView>().Single();

        public ProcessRow Row(int pid) => Table.Rows.Single(r => r.Id.Pid == pid);

        public void Dispose()
        {
            _form.Dispose();
            _images.Dispose(); // the view only borrows the image list, so it goes after the view
        }

        private static RawProcess P(int pid, string name, long privateWorkingSet = 0) =>
            new(pid, 0, 100 + pid, name, 0, 0, privateWorkingSet, 0, 1, 1, 1, 8, false);
    }

    private static int[] Pids(DetailsView view) => [.. view.DisplayedRows.Select(r => r.Id.Pid)];

    private static int ColumnIndex(ProcessListView list, string title) =>
        list.Columns.Cast<ColumnHeader>().Single(c => c.Text == title).Index;

    private static unsafe string[] TitlesInDisplayOrder(ProcessListView list)
    {
        var order = new int[list.Columns.Count];
        fixed (int* p = order)
            Assert.NotEqual(0, User32.SendMessage(list.Handle, LvmGetColumnOrderArray, order.Length, (nint)p));
        return [.. order.Select(i => list.Columns[i].Text)];
    }

    private static nint Header(ProcessListView list) => User32.SendMessage(list.Handle, LvmGetHeader, 0, 0);

    private static int SortBits(ProcessListView list, int column)
    {
        var item = new User32.HdItem { Mask = HdiFormat };
        Assert.NotEqual(0, User32.SendMessage(Header(list), HdmGetItemW, column, ref item));
        return item.Format & (HdfSortUp | HdfSortDown);
    }

    private static unsafe string ItemText(ProcessListView list, int row, int subItem)
    {
        var buffer = stackalloc char[256];
        var item = new User32.LvItem { SubItem = subItem, Text = (nint)buffer, TextMax = 256 };
        var length = (int)User32.SendMessage(list.Handle, LvmGetItemTextW, row, ref item);
        return new string(buffer, 0, length);
    }

    private static int ItemImage(ProcessListView list, int row)
    {
        var item = new User32.LvItem { Mask = LvifImage, Item = row };
        Assert.NotEqual(0, User32.SendMessage(list.Handle, LvmGetItemW, 0, ref item));
        return item.Image;
    }

    private static unsafe int Find(ProcessListView list, string prefix, int start)
    {
        fixed (char* text = prefix)
        {
            var info = new LvFindInfo { Flags = LvfiString | LvfiPartial | LvfiWrap, Text = text };
            return (int)User32.SendMessage(list.Handle, LvmFindItemW, start, (nint)(&info));
        }
    }

    /// <summary>
    /// The notifications of a header click: the list view tells its parent the column was clicked, then the header tells the list view
    /// it released the mouse capture. WinForms raises <see cref="ListView.ColumnClick"/> on the second.
    /// </summary>
    private static unsafe void ClickHeader(Rig rig, int column)
    {
        var list = rig.List;
        var click = new NmListView { Hdr = new NmHdr { HwndFrom = list.Handle, Code = LvnColumnClick }, Item = -1, SubItem = column };
        User32.SendMessage(rig.View.Handle, WmNotify, 0, (nint)(&click));
        var released = new NmHdr { HwndFrom = Header(list), Code = NmReleasedCapture };
        User32.SendMessage(list.Handle, WmNotify, 0, (nint)(&released));
    }

    /// <summary>The notification the header sends the list view when a dragged column is dropped at <paramref name="toDisplayIndex"/>.</summary>
    private static unsafe void EndDrag(ProcessListView list, int column, int toDisplayIndex)
    {
        var item = new User32.HdItem { Mask = HdiOrder, Order = toDisplayIndex };
        var nm = new NmHeader { Hdr = new NmHdr { HwndFrom = Header(list), Code = HdnEndDragW }, Item = column, Pitem = &item };
        User32.SendMessage(list.Handle, WmNotify, 0, (nint)(&nm));
    }

    /// <summary>A right-click on the middle of header item <paramref name="column"/>; returns the screen point used.</summary>
    private static Point RightClickHeader(ProcessListView list, int column)
    {
        var header = Header(list);
        var rect = new User32.NativeRect();
        Assert.NotEqual(0, User32.SendMessage(header, HdmGetItemRect, column, ref rect));
        var point = new User32.NativePoint { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 };
        Assert.True(User32.ClientToScreen(header, ref point));
        User32.SendMessage(header, WmContextMenu, header, (nint)(uint)((point.Y << 16) | (point.X & 0xFFFF)));
        return new Point(point.X, point.Y);
    }

    private static void RightClickHeaderFromKeyboard(ProcessListView list)
    {
        var header = Header(list);
        User32.SendMessage(header, WmContextMenu, header, -1);
    }

    private static unsafe string MenuText(nint menu, uint position)
    {
        var buffer = stackalloc char[64];
        var length = GetMenuString(menu, position, buffer, 64, MfByPosition);
        return new string(buffer, 0, length);
    }

    private static void RecreateHandle(Control control) =>
        typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, null);
}
