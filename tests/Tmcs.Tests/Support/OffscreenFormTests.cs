using System.Runtime.InteropServices;

namespace Tmcs.Tests.Support;

public partial class OffscreenFormTests
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [Fact]
    public void ShowOffscreen_stays_off_every_screen_without_activation_or_taskbar_entry() => Sta.Run(() =>
    {
        using var f = new OffscreenForm();
        f.ShowOffscreen();

        Assert.True(f.Visible);
        Assert.True(f.IsHandleCreated);
        Assert.False(f.ShowInTaskbar);

        Assert.True(GetWindowRect(f.Handle, out var r));
        var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        Assert.Equal(new Point(-32000, -32000), rect.Location);
        foreach (var screen in Screen.AllScreens)
            Assert.False(rect.IntersectsWith(screen.Bounds), $"window rect {rect} overlaps screen {screen.DeviceName} {screen.Bounds}");

        var exStyle = GetWindowLongW(f.Handle, GWL_EXSTYLE);
        Assert.Equal(WS_EX_TOOLWINDOW, exStyle & WS_EX_TOOLWINDOW);
        Assert.Equal(WS_EX_NOACTIVATE, exStyle & WS_EX_NOACTIVATE);

        Assert.NotEqual(f.Handle, GetForegroundWindow());
        Assert.NotSame(f, Form.ActiveForm);
    });
}
