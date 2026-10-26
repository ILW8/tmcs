using System.Runtime.InteropServices;

namespace Tmcs.Tests.Support;

public partial class HiddenDesktopTests
{
    private const string DialogTitle = "tmcs hidden desktop test";
    private const int TDCBF_OK_BUTTON = 0x0001;
    private const int IDOK = 1;
    private const uint TDM_CLICK_BUTTON = 0x0400 + 102; // WM_USER + 102: wParam is the button id
    private const uint UOI_NAME = 2;
    private const uint DESKTOP_READOBJECTS = 0x0001;

    // A modal TaskDialog with only OK runs on the hidden desktop and returns once a posted TDM_CLICK_BUTTON(IDOK) reaches it. (It
    // ignores WM_CLOSE: a task dialog without a Cancel button and TDF_ALLOW_DIALOG_CANCELLATION cannot be closed.) The dialog is found with
    // FindWindowW, which searches only the calling thread's desktop. After Run the caller is still on its own desktop, and the hidden
    // one no longer exists: Run closed it, which fails while any thread of the process is still on it.
    [Fact]
    public void A_task_dialog_on_the_hidden_desktop_opens_and_closes()
    {
        string? callerDesktop = DesktopName(GetThreadDesktop(GetCurrentThreadId()));
        string? hiddenDesktop = null;
        int hr = -1, button = 0;
        var watchdogFired = false;
        HiddenDesktop.Run(() =>
        {
            hiddenDesktop = DesktopName(GetThreadDesktop(GetCurrentThreadId()));
            using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            var ticks = 0;
            timer.Tick += (_, _) =>
            {
                var dialog = FindWindowW("#32770", DialogTitle);
                if (dialog == 0) return;
                if (++ticks == 1)
                {
                    PostMessageW(dialog, TDM_CLICK_BUTTON, IDOK, 0);
                }
                else
                {
                    // The watchdog: the dialog ignored the click, so end it rather than hang the run, and fail the test.
                    watchdogFired = true;
                    DestroyWindow(dialog);
                }
            };
            timer.Start();
            hr = TaskDialog(0, 0, DialogTitle, "Hidden desktop", null, TDCBF_OK_BUTTON, 0, out button);
        });

        Assert.Equal(callerDesktop, DesktopName(GetThreadDesktop(GetCurrentThreadId())));
        Assert.False(watchdogFired, "the dialog ignored TDM_CLICK_BUTTON");
        Assert.Equal(0, hr);
        Assert.Equal(IDOK, button);
        Assert.NotNull(hiddenDesktop);
        Assert.NotEqual(callerDesktop, hiddenDesktop);
        AssertDesktopGone(hiddenDesktop!);
    }

    // A WinForms modal form, shown and closed several times, as the affinity dialog will be. With the IME left on, the third show
    // stalled 2 s and left pool threads on the desktop, so Run could not close it.
    [Fact]
    public void A_modal_form_on_the_hidden_desktop_opens_and_closes_without_keeping_the_desktop()
    {
        string? hiddenDesktop = null;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        HiddenDesktop.Run(() =>
        {
            hiddenDesktop = DesktopName(GetThreadDesktop(GetCurrentThreadId()));
            for (var i = 0; i < 5; i++)
            {
                using var form = new Form { Text = DialogTitle };
                form.Controls.Add(new ListView());
                form.Controls.Add(new Button());
                using var timer = new System.Windows.Forms.Timer { Interval = 50 };
                var ticks = 0;
                timer.Tick += (_, _) =>
                {
                    // The first tick closes the form; the watchdog fails the test if it is still open 5 s later.
                    if (++ticks == 1) form.Close();
                    else if (ticks > 100) { form.Dispose(); Assert.Fail("the form ignored Close"); }
                };
                form.Shown += (_, _) => timer.Start();
                form.ShowDialog();
            }
        });

        Assert.True(elapsed.ElapsedMilliseconds < 2000, $"five shows took {elapsed.ElapsedMilliseconds} ms");
        AssertDesktopGone(hiddenDesktop!);
    }

    private static void AssertDesktopGone(string name)
    {
        var reopened = OpenDesktopW(name, 0, false, DESKTOP_READOBJECTS);
        if (reopened != 0) CloseDesktop(reopened);
        Assert.Equal(0, reopened);
    }

    [Fact]
    public void Run_rethrows_what_the_body_throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HiddenDesktop.Run(() => throw new InvalidOperationException("from body")));
        Assert.Equal("from body", ex.Message);
    }

    private static unsafe string? DesktopName(nint desktop)
    {
        var buffer = stackalloc char[256];
        if (!GetUserObjectInformationW(desktop, UOI_NAME, buffer, 256 * sizeof(char), out _)) return null;
        return new string(buffer);
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    private static partial nint GetThreadDesktop(uint threadId);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenDesktopW(string name, uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(nint desktop);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserObjectInformationW(nint obj, uint index, char* info, uint length, out uint needed);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string? className, string? windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    // Bound on first call, inside HiddenDesktop.Run's comctl32 v6 context: TaskDialog exists only in v6.
    [LibraryImport("comctl32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int TaskDialog(nint owner, nint instance, string? title, string? mainInstruction, string? content,
        int buttons, nint icon, out int button);
}
