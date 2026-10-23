using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static partial class User32
{
    /// <summary><c>GR_GDIOBJECTS</c>: count the process's GDI objects.</summary>
    public const uint GrGdiObjects = 0;

    /// <summary><c>LVM_SETIMAGELIST</c>: wParam is an <c>LVSIL_*</c> value, lParam the <c>HIMAGELIST</c>.</summary>
    public const uint LvmSetImageList = 0x1003;

    /// <summary><c>LVM_GETITEMRECT</c>: wParam is the item, lParam a <see cref="NativeRect"/> whose <c>Left</c> holds an <c>LVIR_*</c> value.</summary>
    public const uint LvmGetItemRect = 0x100E;

    /// <summary><c>LVM_SCROLL</c>: lParam is the vertical distance in pixels, which report view rounds to whole rows.</summary>
    public const uint LvmScroll = 0x1014;

    /// <summary><c>LVM_GETHEADER</c>: returns the header control's handle.</summary>
    public const uint LvmGetHeader = 0x101F;

    /// <summary><c>LVM_GETTOPINDEX</c>.</summary>
    public const uint LvmGetTopIndex = 0x1027;

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
}
