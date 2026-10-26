using Tmcs.Actions;

namespace Tmcs.Tests.Support;

public class DialogSoundsTests
{
    const int IDOK = 1;

    [Fact]
    public void An_error_dialog_sound_goes_to_the_silent_stub()
    {
        // The error dialog would play the system's error sound. If the module initializer's patch had failed, the run would have
        // thrown before any test, and this count would stay put while the sound played.
        var before = DialogSounds.Swallowed;
        var fired = false;
        HiddenDesktop.Run(() =>
        {
            using var driver = new TaskDialogDriver(pressOnActivate: true) { Press = IDOK };
            ErrorDialog.Show(null, ErrorKind.EndTask, new(ActionOutcome.EAccessDenied, ShowDialog: true), isElevated: false);
            driver.Closed();
            fired = driver.WatchdogFired;
        });

        var after = DialogSounds.Swallowed;
        Assert.False(fired, "the dialog ignored OK");
        Assert.True(after.Plays + after.Beeps > before.Plays + before.Beeps, "the dialog's sound did not reach a stub");
    }
}
