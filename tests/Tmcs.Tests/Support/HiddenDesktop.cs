using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Tmcs.Tests.Support;

/// <summary>
/// Runs test bodies that show modal UI (a TaskDialog, the affinity dialog) on a desktop of their own. The desktop is never switched
/// to, so nothing the body's thread shows reaches the user's screen, and a modal loop needs no one to answer it.
/// Only windows created by the body's own thread land on the hidden desktop. A window made on another thread, or in another
/// process, appears on the user's normal desktop. So a test must never call, even here, <c>RunNewTask.Show</c> (the shell's Run
/// dialog runs on a thread <c>SHCreateThread</c> starts), the shell's Explorer or Properties windows, or anything else that shows a
/// window from another thread or process.
/// </summary>
internal static partial class HiddenDesktop
{
    private const uint GENERIC_ALL = 0x10000000;
    private const uint COINIT_APARTMENTTHREADED = 0x2;

    /// <summary>A body still running after this long is treated as a hung modal: <see cref="Run"/> fails rather than hang the run.</summary>
    private const int TimeoutMs = 5 * 60 * 1000;

    /// <summary>
    /// Runs <paramref name="body"/> on a new STA thread assigned to a new, never-shown desktop, with comctl32 v6 active as
    /// <see cref="Sta.Run"/> has it, then closes the desktop. Rethrows anything the body threw. The body must end every modal it
    /// shows; each needs its own watchdog.
    /// </summary>
    /// <remarks>
    /// The thread is not moved back to its old desktop before it ends: WinForms and COM keep message-only windows on the thread
    /// (the marshaling control, OLE's apartment window) until it exits, and SetThreadDesktop refuses a thread that has windows.
    /// The thread's exit destroys them and releases the desktop, which is then closed from the calling thread, whose own desktop
    /// never changes.
    /// </remarks>
    public static void Run(Action body)
    {
        var desktop = CreateDesktopW($"tmcs-test-{Guid.NewGuid():N}", 0, 0, 0, GENERIC_ALL, 0);
        if (desktop == 0) throw new Win32Exception();
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                MoveToDesktop(desktop);
                // With the IME on, a modal form here sometimes stalls 2 s in ShowDialog and leaves two of the process's pool threads
                // on this desktop, so it cannot close. No dialog under test takes IME input.
                if (!ImmDisableIME(0)) throw new Win32Exception();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, threadScope: true);
                using (CommonControls6.Activate()) body();
            }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.IsBackground = true; // a hung body must not keep the test host alive
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // A hung body keeps the desktop: it cannot close while the thread is on it. The test host's exit frees it.
        if (!thread.Join(TimeoutMs)) throw new TimeoutException($"The hidden-desktop body ran for more than {TimeoutMs} ms.");
        var closed = CloseDesktop(desktop);
        var closeError = closed ? null : new Win32Exception();
        failure?.Throw();
        if (closeError is not null) throw closeError;
    }

    /// <summary>
    /// Moves the calling STA thread to <paramref name="desktop"/>. SetThreadDesktop fails with ERROR_BUSY once a thread owns a window,
    /// and the runtime's STA initialization has made one (COM's apartment window), so the thread leaves the apartment for the move
    /// and enters it again. The runtime still ends the apartment once at thread exit, which the re-entry balances.
    /// </summary>
    private static void MoveToDesktop(nint desktop)
    {
        CoUninitialize();
        var moved = SetThreadDesktop(desktop);
        var moveError = moved ? null : new Win32Exception();
        var hr = CoInitializeEx(0, COINIT_APARTMENTTHREADED);
        if (moveError is not null) throw moveError;
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    /// <param name="device">Reserved; must be null.</param>
    /// <param name="devMode">Reserved; must be null.</param>
    /// <param name="security">SECURITY_ATTRIBUTES*; null for the default.</param>
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateDesktopW(string name, nint device, nint devMode, uint flags, uint access, nint security);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadDesktop(nint desktop);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(nint desktop);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();
    /// <param name="threadId">0 for the calling thread.</param>
    [LibraryImport("imm32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImmDisableIME(uint threadId);
}
