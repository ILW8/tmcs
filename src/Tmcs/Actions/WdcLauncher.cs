using System.Runtime.InteropServices;
using Tmcs.Native;

namespace Tmcs.Actions;

/// <summary>
/// Starts a command through <c>wdc.dll!WdcRunTaskAsInteractiveUser</c> (reference 2.5): the scheduled task
/// <c>\Microsoft\Windows\Task Manager\Interactive</c> runs it as the signed-in user, without elevation, in the caller's session. Task
/// Manager starts the Run dialog's unchecked commands this way, and a non-elevated caller may run the task too (probe P2), so both the
/// Debug and the Release build use it. wdc.dll is loaded from System32 only, for each use, and freed again, as Task Manager does.
/// </summary>
internal static unsafe class WdcLauncher
{
    /// <summary><c>HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND)</c>.</summary>
    private const int ProcNotFound = unchecked((int)0x8007007F);

    private static readonly Lazy<bool> Available = new(Resolves);

    /// <summary>
    /// Whether wdc.dll loads from System32 and exports <c>WdcRunTaskAsInteractiveUser</c>. Found once, by loading the DLL and
    /// looking the export up without calling it, and kept.
    /// </summary>
    public static bool IsAvailable => Available.Value;

    /// <summary>Starts <paramref name="commandLine"/> as the signed-in user, in <paramref name="directory"/>.</summary>
    /// <param name="commandLine">The command as the user typed it, arguments included.</param>
    /// <param name="directory">The working directory, or null.</param>
    /// <returns>The export's HRESULT, or a failure HRESULT when wdc.dll or the export cannot be found.</returns>
    public static int Run(string commandLine, string? directory)
    {
        var module = Kernel32.LoadLibraryExW("wdc.dll", 0, Kernel32.LoadLibrarySearchSystem32);
        if (module == 0) return Marshal.GetHRForLastWin32Error();
        try
        {
            var run = (delegate* unmanaged<char*, char*, uint, int>)Export(module);
            if (run == null) return ProcNotFound;
            fixed (char* command = commandLine)
            fixed (char* folder = directory)
            {
                // Always 0, never a value from the caller or the Run dialog: probe P3 found that 0 honors the directory, and 0x40
                // runs the task elevated, which raises a UAC prompt (ruling R12).
                return run(command, folder, 0);
            }
        }
        finally
        {
            Kernel32.FreeLibrary(module);
        }
    }

    private static bool Resolves()
    {
        var module = Kernel32.LoadLibraryExW("wdc.dll", 0, Kernel32.LoadLibrarySearchSystem32);
        if (module == 0) return false;
        try
        {
            return Export(module) != 0;
        }
        finally
        {
            Kernel32.FreeLibrary(module);
        }
    }

    private static nint Export(nint module)
    {
        fixed (byte* name = "WdcRunTaskAsInteractiveUser"u8) // a u8 literal is stored with a zero terminator
            return Kernel32.GetProcAddress(module, name);
    }
}
