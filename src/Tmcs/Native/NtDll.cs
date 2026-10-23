using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class NtDll
{
    public const int SystemProcessInformation = 5;

    /// <summary>STATUS_INFO_LENGTH_MISMATCH: the buffer was too small for the snapshot.</summary>
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    /// <returns>An NTSTATUS. Success codes are non-negative.</returns>
    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(int infoClass, void* buffer, int length, out int returnLength);
}
