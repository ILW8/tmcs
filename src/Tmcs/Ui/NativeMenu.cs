using System.ComponentModel;
using Tmcs.Native;

namespace Tmcs.Ui;

/// <summary>
/// Builds a Win32 menu: a menu bar (<see cref="Bar"/>) or a popup (<see cref="Popup"/>). A menu added to another with
/// <see cref="AddPopup"/> belongs to that parent and is destroyed with it, so only a root menu's <see cref="Dispose"/> destroys anything.
/// </summary>
internal sealed class NativeMenu : IDisposable
{
    /// <summary>True once a parent menu owns this one.</summary>
    private bool _owned;

    private NativeMenu(nint handle)
    {
        if (handle == 0) throw new Win32Exception();
        Handle = handle;
    }

    /// <summary>The <c>HMENU</c>; 0 once disposed.</summary>
    public nint Handle { get; private set; }

    /// <summary>An empty menu bar.</summary>
    public static NativeMenu Bar() => new(User32.CreateMenu());

    /// <summary>A popup menu holding <paramref name="items"/>, in order.</summary>
    public static NativeMenu Popup(params (string text, int id)[] items)
    {
        var menu = new NativeMenu(User32.CreatePopupMenu());
        try
        {
            foreach (var (text, id) in items) menu.AddItem(text, id);
        }
        catch
        {
            menu.Dispose();
            throw;
        }

        return menu;
    }

    public NativeMenu AddItem(string text, int id) => Append(0, (nuint)id, text);

    public NativeMenu AddSeparator() => Append(User32.MfSeparator, 0, null);

    /// <summary>Adds <paramref name="child"/> as a submenu. This menu owns it from now on: disposing <paramref name="child"/> no longer destroys it.</summary>
    public NativeMenu AddPopup(string text, NativeMenu child)
    {
        if (child._owned || child == this || child.Handle == 0) throw new ArgumentException("The menu already has a parent, or is this menu, or is disposed.", nameof(child));
        Append(User32.MfPopup, (nuint)child.Handle, text);
        child._owned = true;
        return this;
    }

    /// <summary>Enables or grays the item with command id <paramref name="id"/>.</summary>
    public void SetEnabled(int id, bool enabled) => User32.EnableMenuItem(Handle, (uint)id, enabled ? 0 : User32.MfGrayed);

    /// <summary>Checks or clears the item with command id <paramref name="id"/>, here or in a submenu.</summary>
    public void SetChecked(int id, bool isChecked) => User32.CheckMenuItem(Handle, (uint)id, isChecked ? User32.MfChecked : 0);

    /// <summary>Gives the item <paramref name="id"/> a radio check mark and clears the other items from <paramref name="first"/> to <paramref name="last"/>, all in this menu.</summary>
    public void CheckRadio(int first, int last, int id) => User32.CheckMenuRadioItem(Handle, (uint)first, (uint)last, (uint)id, 0);

    /// <summary>Shows the menu at <paramref name="screen"/> and waits until it closes.</summary>
    /// <returns>The chosen item's id, or 0 when the menu was dismissed.</returns>
    public int Track(Control owner, Point screen) =>
        User32.TrackPopupMenuEx(Handle, User32.TpmReturnCmd | User32.TpmRightButton, screen.X, screen.Y, owner.Handle, 0);

    public void Dispose()
    {
        if (Handle == 0) return;
        if (!_owned) User32.DestroyMenu(Handle);
        Handle = 0;
    }

    private NativeMenu Append(uint flags, nuint id, string? text)
    {
        if (!User32.AppendMenu(Handle, flags, id, text)) throw new Win32Exception();
        return this;
    }
}
