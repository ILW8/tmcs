using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class Kernel32
{
    /// <summary>The weakest process right that still allows image-name, times, command-line and token queries.</summary>
    public const uint ProcessQueryLimitedInformation = 0x1000;

    public const int ErrorInsufficientBuffer = 122;

    /// <returns>The process handle, or 0 on failure. The caller must <see cref="CloseHandle"/> it.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    /// <summary>Times are FILETIMEs (100 ns ticks since 1601, UTC), which have the same layout as a <see cref="long"/>.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(nint process, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

    /// <param name="size">In: capacity of <paramref name="name"/> in chars. Out: chars written, excluding the terminator.</param>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageNameW(nint process, uint flags, char* name, ref uint size);

    /// <returns>A pseudo handle that needs no closing.</returns>
    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();
}
