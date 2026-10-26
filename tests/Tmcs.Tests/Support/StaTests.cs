using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Tmcs.Tests.Support;

public class StaTests
{
    [Fact]
    public void Run_surfaces_exception_thrown_by_a_handler_pumped_by_DoEvents()
    {
        // Without Sta.Run switching the thread to UnhandledExceptionMode.ThrowException, this exception would raise a
        // ThreadExceptionDialog on the user's screen instead of reaching the test. Do not remove that call to see this fail.
        var ex = Assert.Throws<InvalidOperationException>(() => Sta.Run(() =>
        {
            using var timer = new System.Windows.Forms.Timer { Interval = 1 };
            timer.Tick += (_, _) => throw new InvalidOperationException("from Tick");
            timer.Start();
            var deadline = Environment.TickCount64 + 5000;
            while (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(5);
                Application.DoEvents();
            }
            Assert.Fail("Tick handler never ran");
        }));
        Assert.Equal("from Tick", ex.Message);
    }

    private const uint CCM_GETVERSION = 0x2008;

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileNameW(nint hModule, StringBuilder name, uint size);

    // A list view reports the common-controls version it implements: 6 for comctl32 v6, less for the 5.82 in System32.
    [Fact]
    public void List_view_on_an_Sta_thread_comes_from_comctl32_v6()
    {
        nint version = 0;
        Sta.Run(() =>
        {
            using var form = new OffscreenForm();
            var list = new ListView();
            form.Controls.Add(list);
            form.ShowOffscreen();
            version = SendMessageW(list.Handle, CCM_GETVERSION, 0, 0);
        });
        Assert.Equal(6, version);
    }

    // A LibraryImport("comctl32.dll") in tmcs binds to the library NativeLibrary.Load finds from the tmcs assembly.
    [Fact]
    public void Tmcs_comctl32_imports_bind_to_comctl32_v6()
    {
        string? path = null;
        Sta.Run(() =>
        {
            var module = NativeLibrary.Load("comctl32.dll", typeof(MainForm).Assembly, null);
            var name = new StringBuilder(1024);
            Assert.NotEqual(0u, GetModuleFileNameW(module, name, (uint)name.Capacity));
            path = name.ToString();
        });
        Assert.Equal(6, FileVersionInfo.GetVersionInfo(path!).FileMajorPart);
    }
}
