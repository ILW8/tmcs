using Tmcs.Native;

namespace Tmcs.Processes;

/// <summary>
/// Finds processes that own a visible top-level window that has stopped responding.
/// Use from one thread at a time: the callback reaches the result set through a thread-static field.
/// </summary>
internal sealed class HungWindowScanner
{
    // One delegate for the life of the app. It is never collected, so native code can always call it, and a scan allocates nothing for it.
    static readonly User32.EnumWindowsProc Callback = OnWindow;

    [ThreadStatic] static HashSet<int>? _target;

    /// <summary>Replaces the contents of <paramref name="hungPids"/> with the ids of processes that have a hung visible top-level window.</summary>
    public void Scan(HashSet<int> hungPids)
    {
        hungPids.Clear();
        _target = hungPids;
        try { User32.EnumWindows(Callback, 0); }
        finally { _target = null; }
    }

    // An exception here would unwind through native code and kill the process, so this must not throw.
    static bool OnWindow(nint hwnd, nint lParam)
    {
        if (User32.IsWindowVisible(hwnd) && User32.IsHungAppWindow(hwnd) && User32.GetWindowThreadProcessId(hwnd, out var pid) != 0)
            _target?.Add(pid);
        return true;
    }
}
