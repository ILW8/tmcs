using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Tmcs.Native;

namespace Tmcs.Tests.Support;

/// <summary>
/// Answers the task dialogs a test shows on <see cref="HiddenDesktop"/>: it presses a button as soon as a dialog is up, and ends a
/// dialog that ignored the press. Use it on the hidden desktop's thread only; dispose it there too.
/// </summary>
internal sealed unsafe partial class TaskDialogDriver : IDisposable
{
    const int WH_CBT = 5;
    const int HCBT_ACTIVATE = 5;
    const uint TDM_CLICK_BUTTON = 0x0400 + 102; // WM_USER + 102: wParam is the button id

    /// <summary>A dialog still open after a whole tick ignored its press.</summary>
    const int WatchdogMs = 5000;

    [ThreadStatic] static TaskDialogDriver? _current;

    readonly System.Windows.Forms.Timer _watchdog = new() { Interval = WatchdogMs };
    nint _hook;
    nint _open;
    int _opened, _openedAtLastTick;

    /// <summary>The button <see cref="Activated"/> presses on a dialog that only has a window (a <c>TaskDialog</c>, which has no callback).</summary>
    public int Press { get; set; }

    /// <summary>Set once the watchdog had to end a dialog.</summary>
    public bool WatchdogFired { get; private set; }

    /// <summary>
    /// Starts the watchdog. With <paramref name="pressOnActivate"/>, a thread-local CBT hook also presses <see cref="Press"/> on every
    /// dialog the thread activates, for dialogs shown without a callback.
    /// </summary>
    public TaskDialogDriver(bool pressOnActivate)
    {
        if (_current is not null) throw new InvalidOperationException("one driver per thread");
        _current = this;
        _watchdog.Tick += (_, _) =>
        {
            // The same dialog has been open since the last tick: it ignored the press. End it rather than hang the run.
            if (_open != 0 && _opened == _openedAtLastTick)
            {
                WatchdogFired = true;
                DestroyWindow(_open);
                _open = 0;
            }
            _openedAtLastTick = _opened;
        };
        _watchdog.Start();
        if (!pressOnActivate) return;
        _hook = SetWindowsHookExW(WH_CBT, &CbtProc, 0, GetCurrentThreadId());
        if (_hook == 0) throw new Win32Exception();
    }

    /// <summary>
    /// For a dialog's own callback (<c>TDN_CREATED</c>): records <paramref name="dialog"/> for the watchdog and presses
    /// <paramref name="button"/>. The press is posted, so it lands once the dialog runs its loop. It presses a disabled button too.
    /// </summary>
    public void Created(nint dialog, int button)
    {
        Opened(dialog);
        PostMessageW(dialog, TDM_CLICK_BUTTON, button, 0);
    }

    /// <summary>Call when the show returns: the dialog is gone.</summary>
    public void Closed() => _open = 0;

    void Opened(nint dialog)
    {
        _open = dialog;
        _opened++;
    }

    public void Dispose()
    {
        _watchdog.Dispose();
        if (_hook != 0) UnhookWindowsHookEx(_hook);
        _hook = 0;
        _current = null;
    }

    [UnmanagedCallersOnly]
    static nint CbtProc(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code == HCBT_ACTIVATE && _current is { } driver && ClassName(wParam) == "#32770")
            {
                driver.Opened(wParam);
                PostMessageW(wParam, TDM_CLICK_BUTTON, driver.Press, 0);
            }
        }
        catch
        {
            // Nothing may escape into user32. The watchdog ends a dialog that was not pressed.
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    /// <summary>The first window under <paramref name="parent"/>, at any depth, with this class and text; 0 when there is none.</summary>
    public static nint FindDescendant(nint parent, string className, string text)
    {
        nint found = 0;
        EnumChildWindows(parent, (child, _) =>
        {
            if (ClassName(child) != className || WindowText(child) != text) return true;
            found = child;
            return false;
        }, 0);
        return found;
    }

    static string ClassName(nint hwnd)
    {
        var buffer = stackalloc char[64];
        var length = GetClassNameW(hwnd, buffer, 64);
        return new string(buffer, 0, length);
    }

    static string WindowText(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = GetWindowTextW(hwnd, buffer, 256);
        return new string(buffer, 0, length);
    }

    /// <summary>The process's GDI objects, USER objects and kernel handles.</summary>
    public static (int Gdi, int User, int Handles) Counts()
    {
        using var self = Process.GetCurrentProcess();
        var process = Kernel32.GetCurrentProcess();
        return ((int)User32.GetGuiResources(process, User32.GrGdiObjects), (int)User32.GetGuiResources(process, User32.GrUserObjects), self.HandleCount);
    }

    /// <summary>
    /// Asserts that no count grew by more than 5 over <paramref name="baseline"/>. Growth only: a drop is not a leak, and earlier tests'
    /// background threads and finalizers can release handles while a loop runs.
    /// </summary>
    public static void AssertFlat((int Gdi, int User, int Handles) baseline, (int Gdi, int User, int Handles) now)
    {
        Assert.True(now.Gdi - baseline.Gdi <= 5, $"GDI objects grew from {baseline.Gdi} to {now.Gdi}");
        Assert.True(now.User - baseline.User <= 5, $"USER objects grew from {baseline.User} to {now.User}");
        Assert.True(now.Handles - baseline.Handles <= 5, $"handles grew from {baseline.Handles} to {now.Handles}");
    }

    delegate bool EnumChildProc(nint hwnd, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int hookId, delegate* unmanaged<int, nint, nint, nint> proc, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowEnabled(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumChildWindows(nint parent, EnumChildProc callback, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetClassNameW(nint hwnd, char* name, int capacity);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextW(nint hwnd, char* text, int capacity);
}
