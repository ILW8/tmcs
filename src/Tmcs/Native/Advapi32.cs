using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class Advapi32
{
    public const uint TokenQuery = 0x0008;
    public const uint TokenAdjustPrivileges = 0x0020;

    public const int TokenUser = 1;
    public const int TokenElevation = 20;
    public const int TokenVirtualizationAllowed = 23;
    public const int TokenVirtualizationEnabled = 24;

    public const uint SePrivilegeEnabled = 0x00000002;
    public const int ErrorNotAllAssigned = 1300;

    /// <summary>The fixed head of the variable-length <c>TOKEN_USER</c> block; <see cref="Sid"/> points into the same block.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TokenUserHead
    {
        public byte* Sid;
        public uint Attributes;
    }

    /// <summary>A <c>LUID</c>: two 4-byte halves, so 4-byte aligned (a <see cref="long"/> would pad the enclosing struct).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    /// <summary><c>TOKEN_PRIVILEGES</c> holding exactly one privilege.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TokenPrivileges1
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint process, uint desiredAccess, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(nint token, int infoClass, void* info, uint length, out uint returnLength);

    /// <param name="nameChars">In: capacity of <paramref name="name"/>. Out: chars written without the terminator, or the size needed (with terminator) on ERROR_INSUFFICIENT_BUFFER.</param>
    /// <param name="domainChars">Same as <paramref name="nameChars"/>, for <paramref name="domain"/>.</param>
    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupAccountSidW(string? systemName, byte* sid, char* name, ref uint nameChars, char* domain, ref uint domainChars, out int sidUse);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupPrivilegeValueW(string? systemName, string name, out Luid luid);

    /// <remarks>Returns true even when the privilege was not granted. Check the last error for <see cref="ErrorNotAllAssigned"/>.</remarks>
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, TokenPrivileges1* newState, uint bufferLength, void* previousState, uint* returnLength);
}
