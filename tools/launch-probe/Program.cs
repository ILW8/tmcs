using System.Runtime.InteropServices;

namespace LaunchProbe;

/// <summary>
/// Reports how it was started, for the Run new task probes: appends one line to the file named by its first argument (or by
/// <c>%TMCS_PROBE_OUT%</c>) with its token elevation, session id, current directory, <c>STARTUPINFO.wShowWindow</c> and full command
/// line, then exits with 0. It opens no window. Exits with 1 when it has nowhere to write.
/// </summary>
internal static partial class Program
{
    /// <summary>TOKEN_INFORMATION_CLASS.TokenElevation.</summary>
    const int TokenElevation = 20;

    const uint TOKEN_QUERY = 0x0008;

    /// <summary>STARTF_USESHOWWINDOW: wShowWindow is meaningful only when this flag is set.</summary>
    const uint StartfUseShowWindow = 0x00000001;

    static int Main(string[] args)
    {
        var path = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("TMCS_PROBE_OUT");
        if (string.IsNullOrEmpty(path)) return 1;

        GetStartupInfoW(out var startup);
        ProcessIdToSessionId((uint)Environment.ProcessId, out var session);
        var line = string.Join('\t',
            $"elevated={Elevated()}",
            $"session={session}",
            $"cwd={Environment.CurrentDirectory}",
            $"startfUseShowWindow={((startup.dwFlags & StartfUseShowWindow) != 0 ? 1 : 0)}",
            $"wShowWindow={startup.wShowWindow}",
            $"cmdline={Marshal.PtrToStringUni(GetCommandLineW())}");
        File.AppendAllText(path, line + Environment.NewLine);
        return 0;
    }

    /// <summary>1 if the token is elevated, 0 if not, or -1 with the Win32 error if it cannot be read.</summary>
    static string Elevated()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token)) return $"-1 (error {Marshal.GetLastPInvokeError()})";
        try
        {
            return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(uint), out _)
                ? (elevated != 0 ? "1" : "0")
                : $"-1 (error {Marshal.GetLastPInvokeError()})";
        }
        finally { CloseHandle(token); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInfo
    {
        public uint cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public nint lpReserved2;
        public nint hStdInput, hStdOutput, hStdError;
    }

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void GetStartupInfoW(out StartupInfo info);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetCommandLineW();

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int infoClass, out uint info, uint length, out uint returned);
}
