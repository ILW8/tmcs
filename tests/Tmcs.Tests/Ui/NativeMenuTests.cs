using System.Runtime.InteropServices;
using Tmcs.Ui;

namespace Tmcs.Tests.Ui;

// No test calls Track: it would open a real popup menu on the owner's screen.
public partial class NativeMenuTests
{
    // Values the tests check are written out here rather than taken from User32, so a wrong production constant shows up.
    private const uint MfByPosition = 0x400;
    private const uint MfGrayed = 0x1;
    private const uint MfPopup = 0x10;
    private const uint MfSeparator = 0x800;

    [LibraryImport("user32.dll")]
    private static partial int GetMenuItemCount(nint menu);

    [LibraryImport("user32.dll")]
    private static partial uint GetMenuItemID(nint menu, int position);

    [LibraryImport("user32.dll")]
    private static partial uint GetMenuState(nint menu, uint item, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetSubMenu(nint menu, int position);

    [LibraryImport("user32.dll", EntryPoint = "GetMenuStringW")]
    private static unsafe partial int GetMenuString(nint menu, uint item, char* buffer, int capacity, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsMenu(nint menu);

    [Fact]
    public void Popup_appends_items_in_order_with_their_ids()
    {
        using var menu = NativeMenu.Popup(("Hide column", 7), ("Select columns", 9));

        Assert.True(IsMenu(menu.Handle));
        Assert.Equal(2, GetMenuItemCount(menu.Handle));
        Assert.Equal(new[] { 7u, 9u }, new[] { GetMenuItemID(menu.Handle, 0), GetMenuItemID(menu.Handle, 1) });
        Assert.Equal(new[] { "Hide column", "Select columns" }, new[] { Text(menu.Handle, 0), Text(menu.Handle, 1) });
        Assert.Equal(0u, GetMenuState(menu.Handle, 0, MfByPosition) & (MfGrayed | MfSeparator | MfPopup));
    }

    [Fact]
    public void AddSeparator_and_AddItem_chain_onto_the_same_menu()
    {
        using var menu = NativeMenu.Popup().AddItem("A", 1).AddSeparator().AddItem("B", 2);

        Assert.Equal(3, GetMenuItemCount(menu.Handle));
        Assert.Equal(MfSeparator, GetMenuState(menu.Handle, 1, MfByPosition) & MfSeparator);
        Assert.Equal(2u, GetMenuItemID(menu.Handle, 2));
    }

    [Fact]
    public void SetEnabled_grays_and_restores_an_item_by_command_id()
    {
        using var menu = NativeMenu.Popup(("A", 1), ("B", 2));

        menu.SetEnabled(2, false);
        Assert.Equal(MfGrayed, GetMenuState(menu.Handle, 2, 0) & MfGrayed);
        Assert.Equal(0u, GetMenuState(menu.Handle, 1, 0) & MfGrayed);

        menu.SetEnabled(2, true);
        Assert.Equal(0u, GetMenuState(menu.Handle, 2, 0) & MfGrayed);
    }

    [Fact]
    public void Bar_holds_popups_that_it_destroys_with_itself()
    {
        var bar = NativeMenu.Bar();
        var file = NativeMenu.Popup(("Exit", 1));
        var child = file.Handle;

        Assert.Same(bar, bar.AddPopup("&File", file));
        Assert.Equal(1, GetMenuItemCount(bar.Handle));
        Assert.Equal(child, GetSubMenu(bar.Handle, 0));
        Assert.Equal("&File", Text(bar.Handle, 0));

        file.Dispose(); // owned by the bar now: must not destroy the submenu
        Assert.True(IsMenu(child));
        Assert.Equal(child, GetSubMenu(bar.Handle, 0));

        var root = bar.Handle;
        bar.Dispose();
        Assert.False(IsMenu(root));
        Assert.False(IsMenu(child));
        bar.Dispose(); // a second call does nothing
    }

    [Fact]
    public void Dispose_destroys_a_root_popup_once()
    {
        var menu = NativeMenu.Popup(("A", 1));
        var handle = menu.Handle;

        menu.Dispose();

        Assert.False(IsMenu(handle));
        Assert.Equal(0, menu.Handle);
        menu.Dispose();
    }

    [Fact]
    public void A_menu_can_have_only_one_parent()
    {
        using var bar = NativeMenu.Bar();
        using var other = NativeMenu.Bar();
        var child = NativeMenu.Popup(("A", 1));
        bar.AddPopup("X", child);

        Assert.Throws<ArgumentException>(() => other.AddPopup("Y", child));
        Assert.Equal(0, GetMenuItemCount(other.Handle));
    }

    private static unsafe string Text(nint menu, uint position)
    {
        var buffer = stackalloc char[64];
        var length = GetMenuString(menu, position, buffer, 64, MfByPosition);
        return new string(buffer, 0, length);
    }
}
