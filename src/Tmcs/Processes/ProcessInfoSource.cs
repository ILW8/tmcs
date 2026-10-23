using System.Runtime.InteropServices;
using Tmcs.Native;

namespace Tmcs.Processes;

/// <summary>
/// Reads a process's image path, command line, user name and UAC virtualization state with the Win32 and NT APIs.
/// Every process and token handle opened here is closed before <see cref="Query"/> returns.
/// </summary>
internal sealed unsafe class ProcessInfoSource : IProcessInfoSource
{
    // The idle process (0) and System (4) have no image, command line or token that can be opened.
    static readonly ProcessStaticInfo SystemProcess = new(null, null, "SYSTEM", UacVirtualization.NotAllowed);

    // A long path can have 32767 chars. Start small on the stack and fall back to a heap buffer of the maximum.
    const int StackPathChars = 1024;
    const int MaxPathChars = 32768;

    // A command line is at most 32767 chars. Anything the API claims to need beyond this is treated as garbage.
    const int StackCommandLineBytes = 1024;
    const uint MaxCommandLineBytes = 1 << 20;

    readonly SidNameCache _sids;

    public ProcessInfoSource(SidNameCache sids) => _sids = sids;

    /// <summary>
    /// Enables <c>SeDebugPrivilege</c> on this process's token, which lets the queries below open processes owned by other accounts.
    /// Returns false when the token does not hold the privilege (not elevated) or the call fails. Never throws.
    /// </summary>
    public static bool TryEnableDebugPrivilege()
    {
        nint token = 0;
        try
        {
            if (!Advapi32.OpenProcessToken(Kernel32.GetCurrentProcess(), Advapi32.TokenAdjustPrivileges | Advapi32.TokenQuery, out token)) return false;
            if (!Advapi32.LookupPrivilegeValueW(null, "SeDebugPrivilege", out var luid)) return false;

            var privileges = new Advapi32.TokenPrivileges1 { PrivilegeCount = 1, Luid = luid, Attributes = Advapi32.SePrivilegeEnabled };
            // AdjustTokenPrivileges succeeds even when it grants nothing. ERROR_NOT_ALL_ASSIGNED is how it says the token lacks the privilege.
            return Advapi32.AdjustTokenPrivileges(token, false, &privileges, 0, null, null)
                   && Marshal.GetLastPInvokeError() != Advapi32.ErrorNotAllAssigned;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (token != 0) Kernel32.CloseHandle(token);
        }
    }

    public ProcessStaticInfo Query(ProcessIdentity id)
    {
        try
        {
            return QueryCore(id);
        }
        catch (Exception)
        {
            // Query is called while sampling and must never throw. The native calls below report failure by return value,
            // so this is only a last resort for something unforeseen.
            return ProcessStaticInfo.Blank;
        }
    }

    ProcessStaticInfo QueryCore(ProcessIdentity id)
    {
        if (id.Pid is 0 or 4) return SystemProcess;

        var process = Kernel32.OpenProcess(Kernel32.ProcessQueryLimitedInformation, false, id.Pid);
        if (process == 0) return ProcessStaticInfo.Blank;
        try
        {
            // The PID may have been reused since the snapshot. Never report another process's details.
            if (!Kernel32.GetProcessTimes(process, out var created, out _, out _, out _) || created != id.CreateTime)
                return ProcessStaticInfo.Blank;

            var (userName, uac) = QueryToken(process);
            return new ProcessStaticInfo(QueryImagePath(process), QueryCommandLine(process), userName, uac);
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    static string? QueryImagePath(nint process)
    {
        var stack = stackalloc char[StackPathChars];
        uint size = StackPathChars;
        if (Kernel32.QueryFullProcessImageNameW(process, 0, stack, ref size)) return new string(stack, 0, (int)size);
        if (Marshal.GetLastPInvokeError() != Kernel32.ErrorInsufficientBuffer) return null;

        var heap = new char[MaxPathChars];
        fixed (char* p = heap)
        {
            size = MaxPathChars;
            return Kernel32.QueryFullProcessImageNameW(process, 0, p, ref size) ? new string(p, 0, (int)size) : null;
        }
    }

    static string? QueryCommandLine(nint process)
    {
        // The result is a UNICODE_STRING followed by its characters, and UNICODE_STRING.Buffer points into that same block.
        // Ask once with a small buffer. If the API reports the size it needs, ask again with a buffer of exactly that size.
        Span<byte> buffer = stackalloc byte[StackCommandLineBytes];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            int status;
            uint needed;
            fixed (byte* p = buffer)
            {
                status = NtDll.NtQueryInformationProcess(process, NtDll.ProcessCommandLineInformation, p, (uint)buffer.Length, out needed);
                if (status >= 0) return ReadUnicodeString(p, buffer.Length);
            }
            if (status is not (NtDll.StatusInfoLengthMismatch or NtDll.StatusBufferOverflow or NtDll.StatusBufferTooSmall)
                || needed <= buffer.Length || needed > MaxCommandLineBytes)
                return null;
            buffer = new byte[needed];
        }
        return null;
    }

    static string? ReadUnicodeString(byte* block, int blockLength)
    {
        var s = (NtDll.UnicodeString*)block;
        if (blockLength < sizeof(NtDll.UnicodeString) || s->Length == 0) return null;

        // Trust nothing: the text must lie wholly inside the block the API filled.
        var text = (byte*)s->Buffer;
        if (text < block + sizeof(NtDll.UnicodeString) || text + s->Length > block + blockLength) return null;
        return new string(s->Buffer, 0, s->Length / sizeof(char));
    }

    (string? UserName, UacVirtualization Uac) QueryToken(nint process)
    {
        if (!Advapi32.OpenProcessToken(process, Advapi32.TokenQuery, out var token)) return (null, UacVirtualization.Unknown);
        try
        {
            var sid = ReadUserSid(token);
            return (sid is null ? null : _sids.Resolve(sid), ReadVirtualization(token));
        }
        finally
        {
            Kernel32.CloseHandle(token);
        }
    }

    static byte[]? ReadUserSid(nint token)
    {
        // TOKEN_USER is variable length: ask how big it is, then read it. The call fails by design the first time.
        Advapi32.GetTokenInformation(token, Advapi32.TokenUser, null, 0, out var needed);
        if (Marshal.GetLastPInvokeError() != Kernel32.ErrorInsufficientBuffer || needed < sizeof(Advapi32.TokenUserHead)) return null;

        // The SID pointer inside the block is an absolute address, so it is only valid while the array stays pinned.
        var block = new byte[needed];
        fixed (byte* p = block)
        {
            if (!Advapi32.GetTokenInformation(token, Advapi32.TokenUser, p, needed, out _)) return null;

            var sid = ((Advapi32.TokenUserHead*)p)->Sid;
            if (sid < p + sizeof(Advapi32.TokenUserHead) || sid + 8 > p + block.Length) return null;

            // A SID is revision, sub-authority count, a 6-byte authority, then 4 bytes per sub-authority.
            var length = 8 + 4 * sid[1];
            if (sid + length > p + block.Length) return null;
            return new ReadOnlySpan<byte>(sid, length).ToArray();
        }
    }

    static UacVirtualization ReadVirtualization(nint token)
    {
        if (!TryReadDword(token, Advapi32.TokenVirtualizationAllowed, out var allowed)) return UacVirtualization.Unknown;
        if (allowed == 0) return UacVirtualization.NotAllowed;
        if (!TryReadDword(token, Advapi32.TokenVirtualizationEnabled, out var enabled)) return UacVirtualization.Unknown;
        return enabled != 0 ? UacVirtualization.Enabled : UacVirtualization.Disabled;
    }

    static bool TryReadDword(nint token, int infoClass, out uint value)
    {
        uint v = 0;
        var ok = Advapi32.GetTokenInformation(token, infoClass, &v, sizeof(uint), out _);
        value = v;
        return ok;
    }
}
