using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class NtDll
{
    public const int SystemProcessInformation = 5;

    /// <summary><c>SystemPerformanceInformation</c>: page counts for available memory, commit and the pools.</summary>
    public const int SystemPerformanceInformation = 2;

    /// <summary><c>SystemPageFileInformation</c>: a chain of entries whose names look like <c>\??\C:\pagefile.sys</c>.</summary>
    public const int SystemPageFileInformation = 18;

    /// <summary><c>SystemMemoryListInformation</c>: page counts of the zero, free, modified and standby lists.</summary>
    public const int SystemMemoryListInformation = 80;

    /// <summary><c>SystemTimeOfDayInformation</c>: a 48-byte block whose first two longs are <c>BootTime</c> and <c>CurrentTime</c> (100 ns, FILETIME epoch).</summary>
    public const int SystemTimeOfDayInformation = 3;

    /// <summary><c>ProcessCommandLineInformation</c>: a <see cref="UnicodeString"/> followed by its characters, in one block.</summary>
    public const int ProcessCommandLineInformation = 60;

    /// <summary>STATUS_INFO_LENGTH_MISMATCH: the buffer was too small for the snapshot.</summary>
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    /// <summary>STATUS_BUFFER_OVERFLOW: the buffer held only part of the data.</summary>
    public const int StatusBufferOverflow = unchecked((int)0x80000005);

    /// <summary>STATUS_BUFFER_TOO_SMALL: the buffer was too small for the data.</summary>
    public const int StatusBufferTooSmall = unchecked((int)0xC0000023);

    /// <summary>A <c>UNICODE_STRING</c>. Sequential layout pads it to 16 bytes on x64, as in native code.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnicodeString
    {
        /// <summary>Length of the text in bytes, excluding any terminator.</summary>
        public ushort Length;
        public ushort MaximumLength;
        public char* Buffer;
    }

    /// <returns>An NTSTATUS. Success codes are non-negative.</returns>
    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(int infoClass, void* buffer, int length, out int returnLength);

    /// <returns>An NTSTATUS. Success codes are non-negative.</returns>
    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(nint process, int infoClass, void* buffer, uint length, out uint returnLength);

    /// <summary><c>ProcessBreakOnTermination</c>: a ULONG, non-zero when the process is critical (ending it stops Windows).</summary>
    public const int ProcessBreakOnTermination = 29;

    /// <summary><c>ProcessHandleInformation</c>: a <see cref="ProcessHandleSnapshotHead"/> followed by its <see cref="ProcessHandleTableEntry"/> records.</summary>
    public const int ProcessHandleInformation = 51;

    /// <summary>STATUS_ACCESS_DENIED.</summary>
    public const int StatusAccessDenied = unchecked((int)0xC0000022);

    /// <summary>STATUS_PROCESS_IS_TERMINATING: the process has exited, or is exiting, and has no handle table left to read.</summary>
    public const int StatusProcessIsTerminating = unchecked((int)0xC000010A);

    /// <summary>The fixed head of <c>PROCESS_HANDLE_SNAPSHOT_INFORMATION</c>; the entries follow it in the same block.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessHandleSnapshotHead
    {
        public nuint NumberOfHandles;
        public nuint Reserved;
    }

    /// <summary>A <c>PROCESS_HANDLE_TABLE_ENTRY_INFO</c> (40 bytes on x64).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessHandleTableEntry
    {
        /// <summary>The handle's value in the process that owns it.</summary>
        public nint HandleValue;
        public nuint HandleCount;
        public nuint PointerCount;
        public uint GrantedAccess;
        public uint ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    /// <summary><c>ObjectTypeInformation</c>: a block that starts with the type's name as a <see cref="UnicodeString"/>, whose characters follow in the same block.</summary>
    public const int ObjectTypeInformation = 2;

    /// <returns>An NTSTATUS. Success codes are non-negative.</returns>
    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryObject(nint handle, int infoClass, void* buffer, uint length, out uint returnLength);

    /// <summary>An <c>IO_STATUS_BLOCK</c>: an NTSTATUS (or pointer) slot, then the byte count.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IoStatusBlock
    {
        public nint Status;
        public nuint Information;
    }

    /// <summary><c>FileIsRemoteDeviceInformation</c>: one BOOLEAN, non-zero when the file is on a remote device.</summary>
    public const int FileIsRemoteDeviceInformation = 0x33;

    /// <returns>An NTSTATUS. Success codes are non-negative.</returns>
    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationFile(nint file, out IoStatusBlock ioStatus, void* buffer, uint length, int infoClass);
}
