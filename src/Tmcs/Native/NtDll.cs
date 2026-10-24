using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class NtDll
{
    public const int SystemProcessInformation = 5;

    /// <summary><c>SystemPerformanceInformation</c>: page counts for available memory, commit and the pools.</summary>
    public const int SystemPerformanceInformation = 2;

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
}
