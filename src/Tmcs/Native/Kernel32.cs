using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class Kernel32
{
    /// <summary>The weakest process right that still allows image-name, times, command-line and token queries.</summary>
    public const uint ProcessQueryLimitedInformation = 0x1000;

    public const int ErrorInsufficientBuffer = 122;

    /// <summary><c>ALL_PROCESSOR_GROUPS</c>: <see cref="GetActiveProcessorCount"/> counts the processors of every group.</summary>
    public const ushort AllProcessorGroups = 0xFFFF;

    /// <summary><c>RelationAll</c>: <see cref="GetLogicalProcessorInformationEx"/> returns every relationship.</summary>
    public const int RelationAll = 0xFFFF;

    /// <summary><c>PF_VIRT_FIRMWARE_ENABLED</c>: virtualization is enabled in the firmware.</summary>
    public const uint PfVirtFirmwareEnabled = 21;

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

    /// <param name="process">A handle with at least <see cref="ProcessQueryLimitedInformation"/>.</param>
    /// <param name="flags">0 for a Win32 path, <c>PROCESS_NAME_NATIVE</c> (1) for a native one.</param>
    /// <param name="name">Receives the path, with a terminator.</param>
    /// <param name="size">In: capacity of <paramref name="name"/> in chars. Out: chars written, excluding the terminator.</param>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageNameW(nint process, uint flags, char* name, ref uint size);

    /// <returns>A pseudo handle that needs no closing.</returns>
    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    /// <returns>The number of active logical processors in the group, or 0 on failure.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetActiveProcessorCount(ushort groupNumber);

    /// <summary>Fills <paramref name="buffer"/> with variable-sized <c>SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX</c> records.</summary>
    /// <param name="length">In: bytes in <paramref name="buffer"/>. Out: bytes needed or written. Fails with <see cref="ErrorInsufficientBuffer"/> when too small.</param>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLogicalProcessorInformationEx(int relationship, byte* buffer, ref uint length);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsProcessorFeaturePresent(uint feature);

    /// <summary>The <c>MEMORYSTATUSEX</c> block. <see cref="Length"/> must be set to its size before the call.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    /// <summary>The <c>'RSMB'</c> provider signature for <see cref="GetSystemFirmwareTable"/>.</summary>
    public const uint RawSmbiosProvider = 0x52534D42;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    /// <param name="kilobytes">The installed memory in KB, from the SMBIOS memory devices.</param>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPhysicallyInstalledSystemMemory(out ulong kilobytes);

    /// <returns>The table's size in bytes (what was copied, or what is needed when <paramref name="size"/> is too small), or 0 on failure.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetSystemFirmwareTable(uint provider, uint tableId, byte* buffer, uint size);
}
