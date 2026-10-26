using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class ShlwApi
{
    /// <summary><c>CTF_COINIT_STA</c>: the new thread initializes COM as a single-threaded apartment before the thread procedure runs.</summary>
    public const uint CtfCoInitSta = 0x8;

    /// <summary>Starts a thread that runs <paramref name="threadProc"/> with <paramref name="data"/>.</summary>
    /// <param name="threadProc">A <c>LPTHREAD_START_ROUTINE</c>. It must not let an exception escape.</param>
    /// <param name="flags"><c>CTF_*</c> values.</param>
    /// <param name="callback">Runs on the new thread before the caller resumes, or null for none.</param>
    /// <returns>
    /// True when the thread was started. Without <c>CTF_INSIST</c> a thread that cannot be started does not run
    /// <paramref name="threadProc"/> at all, so on false <paramref name="data"/> is still the caller's.
    /// </returns>
    [LibraryImport("shlwapi.dll", EntryPoint = "SHCreateThread", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShCreateThread(delegate* unmanaged<void*, uint> threadProc, void* data, uint flags, delegate* unmanaged<void*, uint> callback);
}
