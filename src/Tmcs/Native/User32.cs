using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static partial class User32
{
    /// <summary><c>GR_GDIOBJECTS</c>: count the process's GDI objects.</summary>
    public const uint GrGdiObjects = 0;

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
