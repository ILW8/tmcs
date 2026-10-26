using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Tmcs.Actions;
using Tmcs.Details;
using Tmcs.Icons;
using Tmcs.Native;
using Tmcs.Processes;
using Tmcs.Settings;
using Tmcs.Ui;

namespace Tmcs.Tests.Details;

// No test here opens the row menu or a dialog, or ends a real process: the rig replaces DetailsView's TrackRowMenu, Confirm and
// ShowError with recorders, and its actions are a FakeProcessActions.
public partial class DetailsActionsTests
{
    // Values the tests check are written out here rather than taken from production code, so a wrong constant shows up.
    private const uint MfByPosition = 0x400;
    private const uint MfGrayed = 0x1;
    private const uint MfSeparator = 0x800;
    private const uint WmContextMenu = 0x007B;
    private const uint WmKeyDown = 0x0100;
    private const nint VkDelete = 0x2E;
    private const uint LvmGetItemRect = 0x100E;
    private const uint LvmSetItemState = 0x102B;
    private const uint LvisFocused = 0x1;
    private const uint LvisSelected = 0x2;

    [LibraryImport("user32.dll")]
    private static partial nint GetSubMenu(nint menu, int position);

    [LibraryImport("user32.dll")]
    private static partial int GetMenuItemCount(nint menu);

    [LibraryImport("user32.dll")]
    private static partial uint GetMenuItemID(nint menu, int position);

    [LibraryImport("user32.dll")]
    private static partial uint GetMenuState(nint menu, uint item, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMenuStringW")]
    private static unsafe partial int GetMenuString(nint menu, uint item, char* buffer, int capacity, uint flags);

    [Fact]
    public void Menu_shape_matches_the_reference() => Sta.Run(() =>
    {
        using var rig = new Rig();
        using var menu = rig.View.PrepareRowMenu([rig.Row(2)]);
        var handle = menu.Handle;

        Assert.Equal(
            ["&End task", "End process &tree", "", "Set &priority", "Set a&ffinity", "", "&Open file location", "P&roperties", "&Copy"],
            Texts(handle));
        Assert.Equal([2, 5], Enumerable.Range(0, GetMenuItemCount(handle)).Where(i => IsSeparator(handle, i)));
        Assert.Equal(CommandIds.EndTask, (int)GetMenuItemID(handle, 0));
        Assert.Equal(CommandIds.EndProcessTree, (int)GetMenuItemID(handle, 1));
        Assert.Equal(-1, (int)GetMenuItemID(handle, 3)); // a submenu
        Assert.Equal(CommandIds.SetAffinity, (int)GetMenuItemID(handle, 4));
        Assert.Equal(CommandIds.OpenFileLocation, (int)GetMenuItemID(handle, 6));
        Assert.Equal(CommandIds.Properties, (int)GetMenuItemID(handle, 7));
        Assert.Equal(-1, (int)GetMenuItemID(handle, 8));

        var priority = GetSubMenu(handle, 3);
        Assert.Equal(["&Realtime", "&High", "&Above normal", "&Normal", "&Below normal", "&Low"], Texts(priority));
        Assert.Equal([510, 511, 512, 513, 514, 515], Ids(priority));
        Assert.Equal([CommandIds.PriorityRealtime, CommandIds.PriorityLow], [Ids(priority)[0], Ids(priority)[^1]]);

        var copy = GetSubMenu(handle, 8);
        Assert.Equal(["&PID", "&Image path"], Texts(copy));
        Assert.Equal([CommandIds.CopyPid, CommandIds.CopyImagePath], Ids(copy));

        Assert.Equal([true, true, true, true, true, true, true], Enabled(handle));
    });

    [Fact]
    public void Command_ids_are_the_planned_values() => Assert.Equal(
        [101, 500, 501, 510, 515, 520, 521, 522, 530, 531],
        [
            CommandIds.RunNewTask, CommandIds.EndTask, CommandIds.EndProcessTree, CommandIds.PriorityRealtime, CommandIds.PriorityLow,
            CommandIds.SetAffinity, CommandIds.OpenFileLocation, CommandIds.Properties, CommandIds.CopyPid, CommandIds.CopyImagePath,
        ]);

    [Fact]
    public void Multi_select_grays_single_only_items() => Sta.Run(() =>
    {
        using var rig = new Rig();
        using var menu = rig.View.PrepareRowMenu([rig.Row(1), rig.Row(2)]);

        // End task, End process tree, Set priority, Set affinity, Open file location, Properties, Copy.
        Assert.Equal([true, true, false, false, false, false, false], Enabled(menu.Handle));
    });

    [Fact]
    public void No_image_path_grays_open_and_properties() => Sta.Run(() =>
    {
        using var rig = new Rig(noPath: [2]);
        using var menu = rig.View.PrepareRowMenu([rig.Row(2)]);

        Assert.Null(rig.Row(2).Info.ImagePath);
        Assert.Equal([true, true, true, true, false, false, true], Enabled(menu.Handle));
    });

    [Fact]
    public void No_selection_opens_no_menu() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Select();

        RightClick(rig.List, rig.List.PointToScreen(new Point(20, 40)));
        RightClickFromKeyboard(rig.List);

        Assert.Empty(rig.Menus);
        Assert.Empty(rig.Confirms);
        Assert.Empty(rig.Actions.Calls);
    });

    [Fact]
    public void Row_menu_opens_at_the_cursor_or_from_the_keyboard_at_the_focused_row() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Select(3);
        var cursor = rig.List.PointToScreen(new Point(30, 50));

        RightClick(rig.List, cursor);
        RightClickFromKeyboard(rig.List);

        var index = rig.IndexOf(3);
        var rect = new User32.NativeRect(); // Left = LVIR_BOUNDS
        Assert.NotEqual(0, User32.SendMessage(rig.List.Handle, LvmGetItemRect, index, ref rect));
        Assert.Equal([cursor, rig.List.PointToScreen(new Point(rect.Left, rect.Top))], rig.Menus.Select(m => m.Screen));
        Assert.All(rig.Menus, m => Assert.Equal([3], m.Selection.Select(r => r.Id.Pid)));
        Assert.Empty(rig.Actions.Calls); // both menus were dismissed
    });

    [Fact]
    public void Menu_acts_on_the_selection_captured_when_it_opened() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Select(2);
        rig.TrackRowMenu = (_, _) =>
        {
            rig.Select(1, 3); // the list's selection moves while the menu is open
            return CommandIds.EndTask;
        };

        RightClick(rig.List, rig.List.PointToScreen(new Point(20, 40)));

        Assert.Equal([1, 3], rig.View.SelectedIdentities().Select(i => i.Pid).Order());
        Assert.Equal([(ConfirmKind.EndTask, 1, "alpha.exe", false, null)], rig.Confirms);
        Assert.Equal([rig.Row(2).Id], rig.Ended);
        Assert.Empty(rig.Errors);
    });

    [Fact]
    public void A_selected_process_that_exits_before_confirm_is_skipped() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Actions.EndTaskOutcomes[3] = ActionOutcome.Gone; // exited, or its PID was reused, while the dialog was up

        rig.View.OnRowCommand(CommandIds.EndTask, [rig.Row(2), rig.Row(3), rig.Row(1)]);
        rig.View.OnRowCommand(CommandIds.EndTask, [rig.Row(3)]);
        rig.View.OnRowCommand(CommandIds.EndProcessTree, [rig.Row(3)]);

        Assert.Equal([rig.Row(2).Id, rig.Row(3).Id, rig.Row(1).Id, rig.Row(3).Id, rig.Row(3).Id], rig.Ended);
        Assert.Empty(rig.Errors);
    });

    [Fact]
    public void Multi_end_confirms_once_and_ends_each() => Sta.Run(() =>
    {
        using var rig = new Rig();

        rig.View.OnRowCommand(CommandIds.EndTask, [rig.Row(2), rig.Row(1)]);

        Assert.Equal([(ConfirmKind.EndTask, 2, null, false, null)], rig.Confirms);
        Assert.Equal([rig.Row(2).Id, rig.Row(1).Id], rig.Ended);

        // End process tree: one call per selected root, each given the table's live rows to find its descendants in.
        rig.Snapshot.Add(new RawProcess(4, 2, 500, "child.exe", 0, 0, 0, 0, 1, 1, 1, 8, false));
        rig.Table.Apply(rig.Snapshot, 20_000_000);
        rig.Actions.Calls.Clear();
        rig.Confirms.Clear();

        rig.View.OnRowCommand(CommandIds.EndProcessTree, [rig.Row(2), rig.Row(1)]);

        Assert.Equal([(ConfirmKind.EndProcessTree, 2, null, false, null)], rig.Confirms);
        Assert.Equal(
            [("IsCritical", rig.Row(2).Id), ("IsCritical", rig.Row(1).Id), ("EndProcessTree", rig.Row(2).Id), ("EndTask", rig.Row(4).Id), ("EndTask", rig.Row(2).Id),
                ("EndProcessTree", rig.Row(1).Id), ("EndTask", rig.Row(1).Id)],
            rig.Actions.Calls);

        // Cancel ends nothing.
        rig.Actions.Calls.Clear();
        rig.ConfirmAnswer = false;
        rig.View.OnRowCommand(CommandIds.EndTask, [rig.Row(2), rig.Row(1)]);
        Assert.Empty(rig.Ended);
        Assert.Empty(rig.Errors);
    });

    [Fact]
    public void One_failure_shows_one_dialog_and_ends_the_others() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Actions.IsElevated = true;
        var denied = new ActionOutcome(ActionOutcome.EAccessDenied, ShowDialog: true);
        var invalid = new ActionOutcome(ActionOutcome.EInvalidArg, ShowDialog: true);
        rig.Actions.EndTaskOutcomes[2] = denied;
        rig.Actions.EndTaskOutcomes[3] = invalid;
        rig.Actions.EndTaskOutcomes[1] = FakeProcessActions.AccessDenied; // reported to no one

        rig.View.OnRowCommand(CommandIds.EndTask, [rig.Row(1), rig.Row(2), rig.Row(3)]);

        Assert.Equal([rig.Row(1).Id, rig.Row(2).Id, rig.Row(3).Id], rig.Ended);
        Assert.Equal([(ErrorKind.EndTask, denied, true)], rig.Errors);

        rig.Errors.Clear();
        rig.Actions.IsElevated = false;
        rig.View.OnRowCommand(CommandIds.EndProcessTree, [rig.Row(3), rig.Row(2)]);
        Assert.Equal([rig.Row(3).Id, rig.Row(2).Id], rig.Actions.Calls.Where(c => c.Action == "EndProcessTree").Select(c => c.Id));
        Assert.Equal([(ErrorKind.EndProcessTree, invalid, false)], rig.Errors);
    });

    [Fact]
    public void A_critical_process_in_the_selection_uses_the_critical_dialog() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Actions.CriticalPids.UnionWith([3, 1]);

        rig.View.OnRowCommand(CommandIds.EndTask, [rig.Row(2), rig.Row(3), rig.Row(1)]);
        rig.View.OnRowCommand(CommandIds.EndTask, [rig.Row(1)]);

        Assert.Equal([(ConfirmKind.EndTask, 3, null, true, "mid.exe"), (ConfirmKind.EndTask, 1, "zeta.exe", true, "zeta.exe")], rig.Confirms);

        // End process tree asks about the selected roots only: a critical descendant does not change the dialog.
        rig.Confirms.Clear();
        rig.Snapshot.Add(new RawProcess(4, 2, 500, "child.exe", 0, 0, 0, 0, 1, 1, 1, 8, false));
        rig.Table.Apply(rig.Snapshot, 20_000_000);
        rig.Actions.CriticalPids.Add(4);

        rig.View.OnRowCommand(CommandIds.EndProcessTree, [rig.Row(2)]);

        Assert.Equal([(ConfirmKind.EndProcessTree, 1, "alpha.exe", false, null)], rig.Confirms);
    });

    [Fact]
    public void Delete_ends_the_selection() => Sta.Run(() =>
    {
        using var rig = new Rig();
        rig.Select(2, 3);

        // As the message loop passes a key down to the focused list: ProcessCmdKey runs up the parent chain.
        var message = Message.Create(rig.List.Handle, (int)WmKeyDown, VkDelete, 0);
        Assert.True(rig.List.PreProcessMessage(ref message));

        Assert.Equal([(ConfirmKind.EndTask, 2, null, false, null)], rig.Confirms);
        Assert.Equal([rig.Row(2).Id, rig.Row(3).Id], rig.Ended);
        Assert.Empty(rig.Menus);

        // With nothing selected, Delete is not handled.
        rig.Select();
        message = Message.Create(rig.List.Handle, (int)WmKeyDown, VkDelete, 0);
        Assert.False(rig.List.PreProcessMessage(ref message));
        Assert.Single(rig.Confirms);
    });

    [Fact]
    public void Thousand_menu_open_and_cancel_stay_flat() => Sta.Run(() =>
    {
        using var rig = new Rig();
        IReadOnlyList<ProcessRow> one = [rig.Row(2)], two = [rig.Row(1), rig.Row(3)];
        for (var i = 0; i < 10; i++) rig.View.PrepareRowMenu(one).Dispose(); // warm up
        var gdi = GuiResources(User32.GrGdiObjects);
        var user = GuiResources(User32.GrUserObjects);
        var handles = HandleCount();

        for (var i = 0; i < 1000; i++)
        {
            using var menu = rig.View.PrepareRowMenu(i % 2 == 0 ? one : two);
        }

        Assert.InRange((int)GuiResources(User32.GrGdiObjects), (int)gdi - 5, (int)gdi + 5);
        Assert.InRange((int)GuiResources(User32.GrUserObjects), (int)user - 5, (int)user + 5);
        Assert.InRange(HandleCount(), handles - 5, handles + 5);
    });

    private static uint GuiResources(uint flags) => User32.GetGuiResources(Kernel32.GetCurrentProcess(), flags);

    private static int HandleCount()
    {
        using var self = Process.GetCurrentProcess();
        return self.HandleCount;
    }

    /// <summary>A right-click on the rows at <paramref name="screen"/>: the list view sends itself this, with wParam = the list.</summary>
    private static void RightClick(ProcessListView list, Point screen) =>
        User32.SendMessage(list.Handle, WmContextMenu, list.Handle, (nint)(uint)((screen.Y << 16) | (screen.X & 0xFFFF)));

    private static void RightClickFromKeyboard(ProcessListView list) => User32.SendMessage(list.Handle, WmContextMenu, list.Handle, -1);

    private static bool IsSeparator(nint menu, int position) => (GetMenuState(menu, (uint)position, MfByPosition) & MfSeparator) != 0;

    /// <summary>Whether each item is enabled, in order, separators left out (a separator reads as grayed).</summary>
    private static bool[] Enabled(nint menu) =>
    [
        .. Enumerable.Range(0, GetMenuItemCount(menu))
            .Where(i => !IsSeparator(menu, i))
            .Select(i => (GetMenuState(menu, (uint)i, MfByPosition) & MfGrayed) == 0),
    ];

    private static int[] Ids(nint menu) => [.. Enumerable.Range(0, GetMenuItemCount(menu)).Select(i => (int)GetMenuItemID(menu, i))];

    private static unsafe string[] Texts(nint menu)
    {
        var count = GetMenuItemCount(menu);
        var texts = new string[count];
        var buffer = stackalloc char[64];
        for (var i = 0; i < count; i++)
        {
            var length = GetMenuString(menu, (uint)i, buffer, 64, MfByPosition);
            texts[i] = new string(buffer, 0, length);
        }

        return texts;
    }

    /// <summary>
    /// A details view over three processes, sorted by name (alpha.exe 2, mid.exe 3, zeta.exe 1), hosted in an off-screen form that is
    /// already shown. Its menu, confirmation and error seams record their calls; the confirmation answers <see cref="ConfirmAnswer"/>.
    /// </summary>
    private sealed class Rig : IDisposable
    {
        public readonly List<RawProcess> Snapshot = [P(1, "zeta.exe"), P(2, "alpha.exe"), P(3, "mid.exe")];
        public readonly ProcessTable Table;
        public readonly FakeProcessActions Actions = new();
        public readonly DetailsView View;
        public readonly List<(IReadOnlyList<ProcessRow> Selection, Point Screen)> Menus = [];
        public readonly List<(ConfirmKind Kind, int Count, string? SingleName, bool Critical, string? CriticalName)> Confirms = [];
        public readonly List<(ErrorKind Kind, ActionOutcome Outcome, bool IsElevated)> Errors = [];
        public bool ConfirmAnswer = true;

        /// <summary>What the menu returns; dismissed (0) unless a test sets it.</summary>
        public Func<IReadOnlyList<ProcessRow>, Point, int> TrackRowMenu = (_, _) => 0;

        private readonly NativeImageList _images = new(16);
        private readonly OffscreenForm _form = new() { ClientSize = new Size(1200, 600) };

        public Rig(int[]? noPath = null)
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US"); // this STA thread's own culture
            var info = new InfoSource(id => new ProcessStaticInfo(noPath?.Contains(id.Pid) == true ? null : "C:\\x\\" + id.Pid, "cmd", "u", UacVirtualization.Disabled));
            Table = new ProcessTable(info, 1);
            Table.Apply(Snapshot, 10_000_000);
            View = new DetailsView(Table, _images, AppSettings.CreateDefault(), Actions) { Dock = DockStyle.Fill };
            View.TrackHeaderMenu = (_, _) => throw new InvalidOperationException("the test would have shown the header menu");
            View.ChooseColumns = _ => throw new InvalidOperationException("the test would have shown the dialog");
            View.TrackRowMenu = (selection, screen) =>
            {
                Menus.Add((selection, screen));
                return TrackRowMenu(selection, screen);
            };
            View.Confirm = (kind, count, singleName, critical, criticalName) =>
            {
                Confirms.Add((kind, count, singleName, critical, criticalName));
                return ConfirmAnswer;
            };
            View.ShowError = (kind, outcome, isElevated) => Errors.Add((kind, outcome, isElevated));
            _form.Controls.Add(View);
            _form.ShowOffscreen();
            View.RefreshRows();
        }

        public ProcessListView List => View.Controls.OfType<ProcessListView>().Single();

        public ProcessRow Row(int pid) => Table.Rows.Single(r => r.Id.Pid == pid);

        /// <summary>The identities EndTask was called for, in order (End process tree calls it for each process it ends).</summary>
        public IEnumerable<ProcessIdentity> Ended => Actions.Calls.Where(c => c.Action == "EndTask").Select(c => c.Id);

        public int IndexOf(int pid)
        {
            for (var i = 0; i < View.DisplayedRows.Count; i++)
                if (View.DisplayedRows[i].Id.Pid == pid)
                    return i;
            throw new InvalidOperationException($"PID {pid} is not displayed.");
        }

        /// <summary>Selects exactly <paramref name="pids"/> in the list, and focuses the first of them.</summary>
        public void Select(params int[] pids)
        {
            SetState(-1, 0, LvisSelected | LvisFocused);
            foreach (var pid in pids) SetState(IndexOf(pid), LvisSelected, LvisSelected);
            if (pids.Length > 0) SetState(IndexOf(pids[0]), LvisFocused, LvisFocused);
        }

        public void Dispose()
        {
            _form.Dispose();
            _images.Dispose(); // the view only borrows the image list, so it goes after the view
        }

        private void SetState(int index, uint state, uint mask)
        {
            var item = new User32.LvItem { State = state, StateMask = mask };
            User32.SendMessage(List.Handle, LvmSetItemState, index, ref item);
        }

        private static RawProcess P(int pid, string name) => new(pid, 0, 100 + pid, name, 0, 0, 0, 0, 1, 1, 1, 8, false);
    }

    /// <summary>Static info from a function of the identity.</summary>
    private sealed class InfoSource(Func<ProcessIdentity, ProcessStaticInfo> query) : IProcessInfoSource
    {
        public int SidCacheCount => 0;

        public ProcessStaticInfo Query(ProcessIdentity id) => query(id);
    }
}
