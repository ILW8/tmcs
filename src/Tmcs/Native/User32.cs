using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static partial class User32
{
    /// <summary><c>GR_GDIOBJECTS</c>: count the process's GDI objects.</summary>
    public const uint GrGdiObjects = 0;

    /// <summary><c>GR_USEROBJECTS</c>: count the process's USER objects.</summary>
    public const uint GrUserObjects = 1;

    /// <summary><c>LVM_SETIMAGELIST</c>: wParam is an <c>LVSIL_*</c> value, lParam the <c>HIMAGELIST</c>.</summary>
    public const uint LvmSetImageList = 0x1003;

    /// <summary><c>LVM_GETNEXTITEM</c>: wParam is the item to start after (-1 for the first), lParam the <c>LVNI_*</c> flags; returns the item or -1.</summary>
    public const uint LvmGetNextItem = 0x100C;

    /// <summary><c>LVM_GETITEMRECT</c>: wParam is the item, lParam a <see cref="NativeRect"/> whose <c>Left</c> holds an <c>LVIR_*</c> value on entry; returns nonzero on success.</summary>
    public const uint LvmGetItemRect = 0x100E;

    /// <summary><c>LVIR_BOUNDS</c>: the whole row, every column included.</summary>
    public const int LvirBounds = 0;

    /// <summary><c>LVM_GETHEADER</c>: returns the header control's handle.</summary>
    public const uint LvmGetHeader = 0x101F;

    /// <summary><c>LVM_SETITEMSTATE</c>: wParam is the item (-1 for every item), lParam an <see cref="LvItem"/> with <c>State</c> and <c>StateMask</c>.</summary>
    public const uint LvmSetItemState = 0x102B;

    /// <summary><c>LVM_GETITEMSTATE</c>: wParam is the item, lParam the state mask; returns the item's state bits under the mask.</summary>
    public const uint LvmGetItemState = 0x102C;

    /// <summary><c>LVM_GETSELECTEDCOUNT</c>.</summary>
    public const uint LvmGetSelectedCount = 0x1032;

    /// <summary><c>LVM_SETSELECTIONMARK</c>: lParam is the item that anchors Shift-selection.</summary>
    public const uint LvmSetSelectionMark = 0x1043;

    /// <summary>
    /// WinForms' <c>WM_REFLECT</c> (0x2000) + <c>WM_NOTIFY</c>: a notification a control sent its parent, which the parent sends back
    /// to the control. lParam starts with an <see cref="NmHdr"/>.
    /// </summary>
    public const int WmReflectNotify = 0x204E;

    /// <summary><c>WM_DEVICECHANGE</c>: broadcast to every top-level window without registering. wParam is a <c>DBT_*</c> event.</summary>
    public const int WmDeviceChange = 0x0219;

    /// <summary><c>DBT_DEVNODES_CHANGED</c>: a device was added to or removed from the system.</summary>
    public const nint DbtDevNodesChanged = 0x0007;

    /// <summary><c>DBT_DEVICEARRIVAL</c>.</summary>
    public const nint DbtDeviceArrival = 0x8000;

    /// <summary><c>DBT_DEVICEREMOVECOMPLETE</c>.</summary>
    public const nint DbtDeviceRemoveComplete = 0x8004;

    /// <summary><c>LVN_GETDISPINFOW</c>: an owner-data list view asks for an item's text, image or indent. lParam is an <see cref="NmLvDispInfo"/>.</summary>
    public const int LvnGetDispInfoW = -177;

    /// <summary><c>LVN_ODCACHEHINT</c>: an owner-data list view names the rows it is about to draw. lParam is an <c>NMLVCACHEHINT</c>.</summary>
    public const int LvnOdCacheHint = -113;

    /// <summary>
    /// <c>NM_CUSTOMDRAW</c>: lParam is an <see cref="NmLvCustomDraw"/> from a list view; the result is a set of <c>CDRF_*</c> flags.
    /// Named for the code, as <see cref="NmCustomDraw"/> is the struct.
    /// </summary>
    public const int NmCustomDrawCode = -12;

    /// <summary><c>CDDS_PREPAINT</c>: the paint cycle starts.</summary>
    public const uint CddsPrePaint = 0x1;

    /// <summary><c>CDDS_ITEMPREPAINT</c>: one item is about to be drawn.</summary>
    public const uint CddsItemPrePaint = 0x10001;

    /// <summary><c>CDRF_NEWFONT</c>: the control draws the item with the colors, and the font in the DC, that the answer leaves.</summary>
    public const nint CdrfNewFont = 0x2;

    /// <summary><c>CDRF_NOTIFYITEMDRAW</c>: answered at <c>CDDS_PREPAINT</c>, the control sends <c>CDDS_ITEMPREPAINT</c> for each item.</summary>
    public const nint CdrfNotifyItemDraw = 0x20;

    /// <summary><c>LVIF_TEXT</c>: <see cref="LvItem.Text"/> is valid, or asked for.</summary>
    public const uint LvifText = 0x1;

    /// <summary><c>LVIF_IMAGE</c>: <see cref="LvItem.Image"/> is valid, or asked for.</summary>
    public const uint LvifImage = 0x2;

    /// <summary><c>LVIF_INDENT</c>: <see cref="LvItem.Indent"/> is valid, or asked for.</summary>
    public const uint LvifIndent = 0x10;

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

    /// <summary>
    /// <c>MF_STRING</c>: the item is text. <c>MF_STRING</c>, <see cref="MfByCommand"/> and <c>MF_ENABLED</c> are all 0, the
    /// defaults the other flags build on.
    /// </summary>
    public const uint MfString = 0x0;

    /// <summary><c>MF_BYCOMMAND</c>: the item is named by its command id, which also searches submenus.</summary>
    public const uint MfByCommand = 0x0;

    /// <summary><c>MF_BYPOSITION</c>: the item is named by its zero-based position in the menu. A submenu's item can only be named so.</summary>
    public const uint MfByPosition = 0x400;

    /// <summary><c>MF_GRAYED</c>.</summary>
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

    /// <summary><c>SW_SHOWNOACTIVATE</c>: shows a window at its normal size and position, restoring a minimized one, without activating it.</summary>
    public const int SwShowNoActivate = 4;

    /// <summary><c>SW_SHOWMINNOACTIVE</c>: minimizes a window without activating it.</summary>
    public const int SwShowMinNoActive = 7;

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

    /// <summary><c>HWND_BOTTOM</c>: moves a window to the bottom of the z-order, out of the topmost band.</summary>
    public const nint HwndBottom = 1;

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

    /// <summary>
    /// <c>LVITEMW</c>. <see cref="Mask"/> says which fields a get fills or a set applies. 88 bytes on x64: the pointer-sized fields
    /// are 8-aligned, so 4 bytes of padding follow <see cref="StateMask"/> (<see cref="Text"/> is at 24) and <see cref="Columns"/>
    /// (<see cref="ColumnIndexes"/> is at 64), and 4 more end the struct after <see cref="Group"/>.
    /// </summary>
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

    /// <summary>
    /// <c>NMHDR</c>, the head of every <c>WM_NOTIFY</c>. 24 bytes on x64: two 8-byte fields, then <see cref="Code"/> at 16 and 4 bytes
    /// of padding. <see cref="Code"/> is a <c>UINT</c> in C; it is signed here because the notification codes are negative.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NmHdr
    {
        public nint HwndFrom;
        public nuint IdFrom;
        public int Code;
    }

    /// <summary><c>NMLVDISPINFOW</c>: <see cref="Item"/> starts at 24 on x64, right after the header, and the whole is 112 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NmLvDispInfo
    {
        public NmHdr Hdr;
        public LvItem Item;
    }

    /// <summary>
    /// <c>NMCUSTOMDRAW</c>. 80 bytes on x64: 4 bytes of padding follow <see cref="DrawStage"/> (<see cref="Hdc"/> is at 32) and
    /// <see cref="ItemState"/> (<see cref="ItemParam"/> is at 72).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NmCustomDraw
    {
        public NmHdr Hdr;
        public uint DrawStage;
        public nint Hdc;
        public NativeRect Rect;
        public nuint ItemSpec;
        public uint ItemState;
        public nint ItemParam;
    }

    /// <summary>
    /// <c>NMLVCUSTOMDRAW</c>, 136 bytes on x64. The colors are <c>COLORREF</c>s, 0x00BBGGRR: <see cref="ClrText"/> is at 80 and
    /// <see cref="ClrTextBk"/> at 84.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NmLvCustomDraw
    {
        public NmCustomDraw Nmcd;
        public int ClrText;
        public int ClrTextBk;
        public int SubItem;
        public uint ItemType;
        public int ClrFace;
        public int IconEffect;
        public int IconPhase;
        public int PartId;
        public int StateId;
        public NativeRect TextRect;
        public uint Align;
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

    /// <param name="menu">The menu bar or popup menu to append to.</param>
    /// <param name="flags">The <c>MF_</c> flags: the item's kind and state.</param>
    /// <param name="id">The item's command id, or the submenu handle when <paramref name="flags"/> has <see cref="MfPopup"/>.</param>
    /// <param name="text">The item's text, or null for a separator.</param>
    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenu(nint menu, uint flags, nuint id, string? text);

    /// <summary>
    /// Replaces an item. Its kind, state, id and text all come from the arguments, so a check mark or gray state that
    /// <paramref name="flags"/> doesn't repeat is cleared.
    /// </summary>
    /// <param name="menu">The menu that holds the item, or one of its ancestors.</param>
    /// <param name="item">The item's command id with <see cref="MfByCommand"/>.</param>
    /// <param name="flags">The <c>MF_</c> flags: how <paramref name="item"/> names the item, and the new item's kind and state.</param>
    /// <param name="id">The new command id: pass the same one to keep it.</param>
    /// <param name="text">The new text.</param>
    [LibraryImport("user32.dll", EntryPoint = "ModifyMenuW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ModifyMenu(nint menu, uint item, uint flags, nuint id, string? text);

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

    /// <param name="menu">The menu that holds the item, or one of its ancestors.</param>
    /// <param name="item">The item's command id.</param>
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

    /// <returns>Whether the window is maximized.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(nint hwnd);

    /// <returns>Whether the window was visible before.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);
}
