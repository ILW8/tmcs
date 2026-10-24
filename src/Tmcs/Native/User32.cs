using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static partial class User32
{
    /// <summary><c>GR_GDIOBJECTS</c>: count the process's GDI objects.</summary>
    public const uint GrGdiObjects = 0;

    /// <summary><c>LVM_SETIMAGELIST</c>: wParam is an <c>LVSIL_*</c> value, lParam the <c>HIMAGELIST</c>.</summary>
    public const uint LvmSetImageList = 0x1003;

    /// <summary><c>LVM_GETNEXTITEM</c>: wParam is the item to start after (-1 for the first), lParam the <c>LVNI_*</c> flags; returns the item or -1.</summary>
    public const uint LvmGetNextItem = 0x100C;

    /// <summary><c>LVM_GETITEMRECT</c>: wParam is the item, lParam a <see cref="NativeRect"/> whose <c>Left</c> holds an <c>LVIR_*</c> value.</summary>
    public const uint LvmGetItemRect = 0x100E;

    /// <summary><c>LVM_SCROLL</c>: lParam is the vertical distance in pixels, which report view rounds to whole rows.</summary>
    public const uint LvmScroll = 0x1014;

    /// <summary><c>LVM_GETHEADER</c>: returns the header control's handle.</summary>
    public const uint LvmGetHeader = 0x101F;

    /// <summary><c>LVM_GETTOPINDEX</c>.</summary>
    public const uint LvmGetTopIndex = 0x1027;

    /// <summary><c>LVM_SETITEMSTATE</c>: wParam is the item (-1 for every item), lParam an <see cref="LvItem"/> with <c>State</c> and <c>StateMask</c>.</summary>
    public const uint LvmSetItemState = 0x102B;

    /// <summary><c>LVM_GETITEMSTATE</c>: wParam is the item, lParam the state mask; returns the item's state bits under the mask.</summary>
    public const uint LvmGetItemState = 0x102C;

    /// <summary><c>LVM_GETSELECTEDCOUNT</c>.</summary>
    public const uint LvmGetSelectedCount = 0x1032;

    /// <summary><c>LVM_SETSELECTIONMARK</c>: lParam is the item that anchors Shift-selection.</summary>
    public const uint LvmSetSelectionMark = 0x1043;

    /// <summary><c>LVNI_FOCUSED</c>.</summary>
    public const nint LvniFocused = 0x1;

    /// <summary><c>LVNI_SELECTED</c>.</summary>
    public const nint LvniSelected = 0x2;

    /// <summary><c>LVIS_FOCUSED</c>.</summary>
    public const uint LvisFocused = 0x1;

    /// <summary><c>LVIS_SELECTED</c>.</summary>
    public const uint LvisSelected = 0x2;

    /// <summary><c>LVSIL_SMALL</c>.</summary>
    public const nint LvsilSmall = 1;

    /// <summary><c>LVIR_BOUNDS</c>.</summary>
    public const int LvirBounds = 0;

    /// <summary><c>HDM_GETITEMCOUNT</c>.</summary>
    public const uint HdmGetItemCount = 0x1200;

    /// <summary><c>HDM_HITTEST</c>: lParam is an <see cref="HdHitTestInfo"/>; returns the item index, or -1.</summary>
    public const uint HdmHitTest = 0x1206;

    /// <summary><c>HDM_GETITEMW</c>: wParam is the item, lParam an <see cref="HdItem"/>.</summary>
    public const uint HdmGetItemW = 0x120B;

    /// <summary><c>HDM_SETITEMW</c>: wParam is the item, lParam an <see cref="HdItem"/>.</summary>
    public const uint HdmSetItemW = 0x120C;

    /// <summary><c>HDI_FORMAT</c>: <see cref="HdItem.Format"/> is valid.</summary>
    public const uint HdiFormat = 0x4;

    /// <summary><c>HDF_SORTDOWN</c>.</summary>
    public const int HdfSortDown = 0x200;

    /// <summary><c>HDF_SORTUP</c>.</summary>
    public const int HdfSortUp = 0x400;

    /// <summary><c>MF_STRING</c> (0), <c>MF_BYCOMMAND</c> (0) and <c>MF_ENABLED</c> (0) are the defaults these build on.</summary>
    public const uint MfGrayed = 0x1;

    /// <summary><c>MF_CHECKED</c>. <c>MF_UNCHECKED</c> is 0.</summary>
    public const uint MfChecked = 0x8;

    /// <summary><c>MF_POPUP</c>: the item's id is a submenu handle.</summary>
    public const uint MfPopup = 0x10;

    /// <summary><c>MF_SEPARATOR</c>.</summary>
    public const uint MfSeparator = 0x800;

    /// <summary><c>SM_CXSMICON</c>: the recommended small icon width.</summary>
    public const int SmCxSmIcon = 49;

    /// <summary><c>SW_RESTORE</c>: un-minimizes a window. On a maximized window that is not minimized it un-maximizes it.</summary>
    public const int SwRestore = 9;

    /// <summary><c>SWP_NOSIZE</c>.</summary>
    public const uint SwpNoSize = 0x1;

    /// <summary><c>SWP_NOMOVE</c>.</summary>
    public const uint SwpNoMove = 0x2;

    /// <summary><c>SWP_NOACTIVATE</c>.</summary>
    public const uint SwpNoActivate = 0x10;

    /// <summary><c>HWND_TOPMOST</c>: moves a window into the topmost band.</summary>
    public const nint HwndTopmost = -1;

    /// <summary><c>HWND_NOTOPMOST</c>: moves a window out of the topmost band.</summary>
    public const nint HwndNoTopmost = -2;

    /// <summary><c>TPM_RIGHTBUTTON</c>: items can be chosen with either mouse button.</summary>
    public const uint TpmRightButton = 0x2;

    /// <summary><c>TPM_RETURNCMD</c>: return the chosen item's id instead of posting <c>WM_COMMAND</c>.</summary>
    public const uint TpmReturnCmd = 0x100;

    /// <summary><c>POINT</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    /// <summary><c>RECT</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary><c>HDITEMW</c>. <see cref="Mask"/> says which fields a get fills or a set applies.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HdItem
    {
        public uint Mask;
        public int Width;
        public nint Text;
        public nint Bitmap;
        public int TextMax;
        public int Format;
        public nint Param;
        public int Image;
        public int Order;
        public uint Type;
        public nint Filter;
        public uint State;
    }

    /// <summary><c>LVITEMW</c>. <see cref="Mask"/> says which fields a get fills or a set applies.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LvItem
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

    /// <summary><c>HDHITTESTINFO</c>. <see cref="Point"/> is in header client coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HdHitTestInfo
    {
        public NativePoint Point;
        public uint Flags;
        public int Item;
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, ref NativeRect lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, ref HdItem lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, ref HdHitTestInfo lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, ref LvItem lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint hwnd, ref NativePoint point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint hwnd, ref NativePoint point);

    /// <summary>Return <c>true</c> to continue the enumeration.</summary>
    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsHungAppWindow(nint hwnd);

    /// <returns>The id of the thread that created the window, or 0 on failure.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out int processId);

    /// <returns>The number of objects of the kind <paramref name="flags"/> selects, or 0 on failure.</returns>
    [LibraryImport("user32.dll")]
    public static partial uint GetGuiResources(nint process, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    /// <returns>An empty menu bar, or 0 on failure. Destroy it with <see cref="DestroyMenu"/> unless a window owns it.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint CreateMenu();

    /// <returns>An empty popup menu, or 0 on failure. Destroy it with <see cref="DestroyMenu"/> unless a menu owns it.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint CreatePopupMenu();

    /// <param name="id">The item's command id, or the submenu handle when <paramref name="flags"/> has <see cref="MfPopup"/>.</param>
    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenu(nint menu, uint flags, nuint id, string? text);

    /// <returns>The item's previous state, or -1 when the item does not exist.</returns>
    [LibraryImport("user32.dll")]
    public static partial int EnableMenuItem(nint menu, uint item, uint flags);

    /// <summary>Also destroys the menu's submenus.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    /// <returns>With <see cref="TpmReturnCmd"/>: the chosen item's id, or 0 when the menu was dismissed.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);

    /// <summary>
    /// Attaches <paramref name="menu"/> as the window's menu bar (0 detaches it). Windows destroys an attached menu together with
    /// the window, so detach a menu that something else destroys before the window goes.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetMenu(nint hwnd, nint menu);

    /// <param name="flags"><see cref="MfChecked"/> or 0, by command id (which also searches submenus).</param>
    /// <returns>The item's previous check state, or -1 when the item does not exist.</returns>
    [LibraryImport("user32.dll")]
    public static partial int CheckMenuItem(nint menu, uint item, uint flags);

    /// <summary>Gives <paramref name="check"/> a radio check mark and clears the marks of the other items from <paramref name="first"/> to <paramref name="last"/>.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CheckMenuRadioItem(nint menu, uint first, uint last, uint check, uint flags);

    /// <returns>The metric scaled for <paramref name="dpi"/>, or 0 on failure.</returns>
    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    /// <returns>A copy the caller must destroy with <see cref="DestroyIcon"/>, or 0 on failure.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint CopyIcon(nint icon);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    /// <returns>Whether the window was visible before.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);
}
