using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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

    /// <summary><c>PROCESS_TERMINATE</c>.</summary>
    public const uint ProcessTerminate = 0x0001;

    /// <summary><c>PROCESS_DUP_HANDLE</c>: lets <see cref="DuplicateHandle"/> copy or close the process's handles.</summary>
    public const uint ProcessDupHandle = 0x0040;

    /// <summary><c>PROCESS_QUERY_INFORMATION</c>: the full query right, which some NT information classes need.</summary>
    public const uint ProcessQueryInformation = 0x0400;

    /// <summary><c>PROCESS_SET_INFORMATION</c>: lets <see cref="SetPriorityClass"/> change the process's class and <see cref="SetProcessAffinityMask"/> its affinity.</summary>
    public const uint ProcessSetInformation = 0x0200;

    public const int ErrorAccessDenied = 5;

    /// <param name="priorityClass">A <c>*_PRIORITY_CLASS</c> value. Realtime without <c>SeIncreaseBasePriorityPrivilege</c> succeeds but sets High.</param>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetPriorityClass(nint process, uint priorityClass);

    /// <param name="process">A handle with at least <see cref="ProcessQueryLimitedInformation"/>.</param>
    /// <returns>The <c>*_PRIORITY_CLASS</c> value, or 0 on failure.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetPriorityClass(nint process);

    /// <param name="process">A handle with at least <see cref="ProcessQueryLimitedInformation"/>.</param>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessAffinityMask(nint process, out nuint processMask, out nuint systemMask);

    /// <param name="process">A handle with <see cref="ProcessSetInformation"/>.</param>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessAffinityMask(nint process, nuint mask);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(nint process, uint exitCode);

    /// <summary><c>DUPLICATE_CLOSE_SOURCE</c>: close the source handle, in the source process, even if the duplication fails.</summary>
    public const uint DuplicateCloseSource = 1;

    /// <summary><c>DUPLICATE_SAME_ACCESS</c>: the copy gets the source's access, and the access argument is ignored.</summary>
    public const uint DuplicateSameAccess = 2;

    /// <param name="targetHandle">Receives the copy, which the caller must <see cref="CloseHandle"/>.</param>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DuplicateHandle(nint sourceProcess, nint sourceHandle, nint targetProcess, out nint targetHandle, uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint options);

    public const uint FileTypeDisk = 1;
    public const uint FileTypePipe = 3;

    /// <returns>A <c>FILE_TYPE_*</c> value: <see cref="FileTypeDisk"/>, 2 for a character device, <see cref="FileTypePipe"/>, or 0 for unknown or failure.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetFileType(nint file);

    /// <summary><c>WT_EXECUTELONGFUNCTION</c>: the work item may block for a long time, so the pool may add a thread for it.</summary>
    public const uint WtExecuteLongFunction = 0x10;

    /// <param name="function">A <c>LPTHREAD_START_ROUTINE</c>. It must not let an exception escape.</param>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueueUserWorkItem(delegate* unmanaged<void*, uint> function, void* context, uint flags);

    /// <param name="process">A handle with at least <see cref="ProcessQueryLimitedInformation"/>.</param>
    /// <param name="flags">0 for a Win32 path, <c>PROCESS_NAME_NATIVE</c> (1) for a native one.</param>
    /// <param name="name">Receives the path, with a terminator.</param>
    /// <param name="size">In: capacity of <paramref name="name"/> in chars. Out: chars written, excluding the terminator.</param>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageNameW(nint process, uint flags, char* name, ref uint size);

    /// <summary><c>FORMAT_MESSAGE_IGNORE_INSERTS</c>: leave <c>%1</c>-style inserts in the text as they are.</summary>
    public const uint FormatMessageIgnoreInserts = 0x200;

    /// <summary><c>FORMAT_MESSAGE_FROM_SYSTEM</c>: look the message id up in the system's message tables.</summary>
    public const uint FormatMessageFromSystem = 0x1000;

    /// <param name="language">0 for the default language search.</param>
    /// <param name="buffer">Receives the message, with a terminator.</param>
    /// <param name="size">Capacity of <paramref name="buffer"/> in chars.</param>
    /// <param name="arguments">The inserts' values; 0 with <see cref="FormatMessageIgnoreInserts"/>.</param>
    /// <returns>The chars written, excluding the terminator, or 0 on failure.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint FormatMessageW(uint flags, nint source, uint messageId, uint language, char* buffer, uint size, nint arguments);

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

    public const uint FileShareRead = 1;
    public const uint FileShareWrite = 2;
    public const uint OpenExisting = 3;

    /// <summary>Opens a device or volume. Access 0 asks for no data rights, which is enough for the query IOCTLs and needs no elevation.</summary>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    /// <summary>The overload for a path read on every tick: it allocates nothing, on success or failure.</summary>
    /// <param name="fileName">Zero-terminated.</param>
    /// <returns>The handle, or <see cref="InvalidHandleValue"/> on failure. The caller must <see cref="CloseHandle"/> it.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true)]
    public static partial nint CreateFileW(char* fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    public const nint InvalidHandleValue = -1;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(SafeFileHandle device, uint controlCode, void* inBuffer, uint inSize, void* outBuffer, uint outSize, out uint bytesReturned, nint overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(nint device, uint controlCode, void* inBuffer, uint inSize, void* outBuffer, uint outSize, out uint bytesReturned, nint overlapped);

    /// <param name="name">Receives <c>\?\Volume{guid}\</c>, with a terminator.</param>
    /// <returns>A find handle for <see cref="FindVolumeClose"/>, or -1 on failure.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint FindFirstVolumeW(char* name, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindNextVolumeW(nint find, char* name, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindVolumeClose(nint find);

    /// <param name="volumeName">The volume name with its trailing backslash.</param>
    /// <param name="names">Receives a multi-string of mount paths (<c>C:\</c>, folder mounts).</param>
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumePathNamesForVolumeNameW(string volumeName, char* names, uint length, out uint needed);

    /// <returns>Chars written excluding the terminator, or 0 on failure.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetSystemWindowsDirectoryW(char* buffer, uint size);

    /// <summary><c>LOAD_LIBRARY_SEARCH_SYSTEM32</c>: <see cref="LoadLibraryExW"/> looks only in System32.</summary>
    public const uint LoadLibrarySearchSystem32 = 0x800;

    /// <param name="file">A DLL name, looked up as <paramref name="flags"/> say.</param>
    /// <param name="reserved">Must be 0.</param>
    /// <returns>The module, or 0 on failure. The caller must <see cref="FreeLibrary"/> it.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadLibraryExW(string file, nint reserved, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeLibrary(nint module);

    /// <param name="name">The export's name, zero-terminated ANSI.</param>
    /// <returns>The export's address, or 0 when the module has no such export.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GetProcAddress(nint module, byte* name);

    /// <param name="wow64">True when the process is a 32-bit one running on 64-bit Windows.</param>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWow64Process(nint process, [MarshalAs(UnmanagedType.Bool)] out bool wow64);
}
