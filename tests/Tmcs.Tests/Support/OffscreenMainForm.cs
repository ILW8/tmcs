using System.Runtime.InteropServices;
using Tmcs.Native;

namespace Tmcs.Tests.Support;

/// <summary>
/// The real <see cref="MainForm"/>, kept far off-screen, never activated, and absent from the taskbar and Alt-Tab, like
/// <see cref="OffscreenForm"/>. Its only working area lies off every screen, so restoring a saved position can never move it
/// onto the owner's monitor. It minimizes and restores through <c>SetWindowPlacement</c> with no-activate show commands,
/// because setting <see cref="Form.WindowState"/> would activate another window.
/// </summary>
internal sealed partial class OffscreenMainForm : MainForm
{
    /// <summary>The one working area the form sees. No screen overlaps it.</summary>
    public static readonly Rectangle OffscreenArea = new(-32000, -32000, 4000, 4000);

    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WmWindowPosChanging = 0x0046;
    private const int WmCommand = 0x0111;
    private const uint SwpNoZOrder = 0x4;
    private const uint WpfSetMinPosition = 0x1;
    private const uint SwShowNoActivate = 4;
    private const uint SwShowMinNoActive = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary><c>WINDOWPOS</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint Hwnd;
        public nint InsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    /// <summary><c>WINDOWPLACEMENT</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Placement
    {
        public uint Length;
        public uint Flags;
        public uint ShowCmd;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public NativeRect NormalPosition;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out NativeRect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowPlacement(nint hwnd, ref Placement placement);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPlacement(nint hwnd, ref Placement placement);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial nint GetActiveWindow();

    /// <summary>
    /// The <c>SWP_*</c> flags of every z-order change made to this window. A test process runs in the background, and Windows
    /// keeps a background process's windows out of the topmost band: it turns <c>HWND_TOPMOST</c> into a plain position before
    /// the window sees it. So tests can check that a z-order change was made, and how, but not the resulting band.
    /// </summary>
    public readonly List<uint> ZOrderChanges = [];

    public OffscreenMainForm(AppServices services) : base(services)
    {
        ShowInTaskbar = false;
        // Only used when the settings hold no position yet; a saved one goes through the off-screen working area.
        StartPosition = FormStartPosition.Manual;
        Location = OffscreenArea.Location;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate;
            return cp;
        }
    }

    protected override IReadOnlyList<Rectangle> WorkingAreas() => [OffscreenArea];

    protected override unsafe void WndProc(ref Message m)
    {
        if (m.Msg == WmWindowPosChanging)
        {
            var pos = (WindowPos*)m.LParam;
            if ((pos->Flags & SwpNoZOrder) == 0) ZOrderChanges.Add(pos->Flags);
        }

        base.WndProc(ref m);
    }

    /// <summary>The window rectangle as Windows reports it, in screen coordinates.</summary>
    public Rectangle WindowRect
    {
        get
        {
            Assert.True(GetWindowRect(Handle, out var r));
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
    }

    /// <summary>Shows the form (without activating it) and pumps pending messages once.</summary>
    public void ShowOffscreen()
    {
        Show();
        Application.DoEvents();
    }

    /// <summary>Sends a menu command, as choosing the item would.</summary>
    public void Command(int id)
    {
        User32.SendMessage(Handle, WmCommand, id, 0);
        Application.DoEvents();
    }

    /// <summary>Runs the form's command-key handling for <paramref name="keys"/>, as a key press would.</summary>
    public bool PressKey(Keys keys)
    {
        var message = new Message();
        return ProcessCmdKey(ref message, keys);
    }

    public void MinimizeWithoutActivation()
    {
        var placement = CurrentPlacement();
        placement.Flags = WpfSetMinPosition;
        placement.ShowCmd = SwShowMinNoActive;
        placement.MinPosition = new NativePoint { X = -32000, Y = -32000 };
        Assert.True(SetWindowPlacement(Handle, ref placement));
        Application.DoEvents();
        Assert.Equal(FormWindowState.Minimized, WindowState);
    }

    public void RestoreWithoutActivation()
    {
        var placement = CurrentPlacement();
        placement.Flags = 0;
        placement.ShowCmd = SwShowNoActivate;
        Assert.True(SetWindowPlacement(Handle, ref placement));
        Application.DoEvents();
        Assert.Equal(FormWindowState.Normal, WindowState);
    }

    /// <summary>Destroys and recreates the window, as WinForms does when some styles change.</summary>
    public void Recreate()
    {
        RecreateHandle();
        Application.DoEvents();
    }

    /// <summary>The window overlaps no screen, and neither the system nor this thread has made it the active window.</summary>
    public void AssertOffscreenAndInactive()
    {
        var rect = WindowRect;
        foreach (var screen in Screen.AllScreens)
            Assert.False(rect.IntersectsWith(screen.Bounds), $"window rect {rect} overlaps screen {screen.DeviceName} {screen.Bounds}");
        Assert.NotEqual(Handle, GetForegroundWindow());
        Assert.NotEqual(Handle, GetActiveWindow());
    }

    private Placement CurrentPlacement()
    {
        var placement = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
        Assert.True(GetWindowPlacement(Handle, ref placement));
        return placement;
    }
}
