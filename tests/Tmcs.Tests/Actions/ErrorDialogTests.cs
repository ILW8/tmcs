using Tmcs.Actions;

namespace Tmcs.Tests.Actions;

/// <summary>
/// The error dialog of reference 3.3. The text tests read what <see cref="ErrorDialog"/> would show without showing it; every dialog
/// that is shown is shown on <see cref="HiddenDesktop"/> and pressed OK by <see cref="TaskDialogDriver"/>'s hook, since a
/// <c>TaskDialog</c> has no callback.
/// </summary>
public class ErrorDialogTests
{
    const int IDOK = 1;
    const int EFileNotFound = unchecked((int)0x80070002); // HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND)

    static readonly ActionOutcome AccessDenied = new(ActionOutcome.EAccessDenied, ShowDialog: true);

    [Fact]
    public void Titles_follow_the_command()
    {
        Assert.Equal("Unable to terminate process", ErrorDialog.Title(ErrorKind.EndTask));
        Assert.Equal("Unable to terminate process tree", ErrorDialog.Title(ErrorKind.EndProcessTree));
        Assert.Equal("Unable to change priority", ErrorDialog.Title(ErrorKind.SetPriority));
        Assert.Equal("Unable to access or set process affinity", ErrorDialog.Title(ErrorKind.Affinity));
    }

    [Fact]
    public void Access_denied_not_elevated_asks_for_admin()
    {
        var (main, content) = ErrorDialog.Text(AccessDenied, isElevated: false);
        Assert.Equal(ActionStrings.AccessDenied, main);
        Assert.Equal("Access denied.", main);
        Assert.Equal(ActionStrings.NeedAdministrator, content);
        Assert.Equal("You need administrator privileges to complete this task. Close Task Manager, then run it as an administrator and try again.", content);
    }

    [Fact]
    public void Invalid_arg_is_not_valid_for_this_process()
    {
        foreach (var elevated in new[] { false, true })
        {
            var (main, content) = ErrorDialog.Text(new(ActionOutcome.EInvalidArg, ShowDialog: true), elevated);
            Assert.Equal("The operation could not be completed.", main);
            Assert.Equal(ActionStrings.OperationFailed, main);
            Assert.Equal("The operation is not valid for this process.", content);
            Assert.Equal(ActionStrings.NotValidForProcess, content);
        }

        // A process gone since its row was read is the same E_INVALIDARG.
        Assert.Equal((ActionStrings.OperationFailed, ActionStrings.NotValidForProcess), ErrorDialog.Text(ActionOutcome.Gone, false));
    }

    [Fact]
    public void Other_error_uses_format_message()
    {
        var (main, content) = ErrorDialog.Text(new(EFileNotFound, ShowDialog: true), isElevated: false);
        Assert.Equal(ActionStrings.OperationFailed, main);
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.Equal(content.Trim(), content);
        Assert.Equal(ErrorDialog.SystemMessage(EFileNotFound), content);
        Assert.NotEqual(ActionStrings.NotValidForProcess, content);
        Assert.NotEqual(ActionStrings.NeedAdministrator, content);
    }

    [Fact]
    public void Elevated_access_denied_uses_format_message()
    {
        var (main, content) = ErrorDialog.Text(AccessDenied, isElevated: true);
        Assert.Equal(ActionStrings.OperationFailed, main);
        Assert.NotEqual(ActionStrings.NeedAdministrator, content);
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.Equal(ErrorDialog.SystemMessage(ActionOutcome.EAccessDenied), content);
    }

    [Fact]
    public void A_code_without_a_system_message_shows_the_code()
    {
        Assert.Equal("0x8004DEAD", ErrorDialog.SystemMessage(unchecked((int)0x8004DEAD)));
    }

    [Fact]
    public void Error_and_notice_show_and_close()
    {
        var fired = false;
        HiddenDesktop.Run(() =>
        {
            using var driver = new TaskDialogDriver(pressOnActivate: true) { Press = IDOK };
            ErrorDialog.Show(null, ErrorKind.EndTask, AccessDenied, isElevated: false);
            driver.Closed();
            ErrorDialog.ShowNotice(null, ActionStrings.RealtimeNoticeTitle, ActionStrings.RealtimeNotice);
            driver.Closed();
            fired = driver.WatchdogFired;
        });

        Assert.False(fired, "a dialog ignored OK");
    }

    [Fact]
    public void Thousand_open_and_cancel_stay_flat()
    {
        (int Gdi, int User, int Handles) first = default, last = default;
        var fired = false;
        HiddenDesktop.Run(() =>
        {
            using var driver = new TaskDialogDriver(pressOnActivate: true) { Press = IDOK };
            for (var i = 0; i < 1000; i++)
            {
                // Alternate a fixed text and the FormatMessage path.
                ErrorDialog.Show(null, ErrorKind.SetPriority, i % 2 == 0 ? AccessDenied : new(EFileNotFound, true), isElevated: false);
                driver.Closed();
                if (i == 0) first = TaskDialogDriver.Counts();
            }
            last = TaskDialogDriver.Counts();
            fired = driver.WatchdogFired;
        });

        Assert.False(fired, "a dialog ignored OK");
        TaskDialogDriver.AssertFlat(first, last);
    }
}
