using Tmcs.Actions;
using Tmcs.Native;

namespace Tmcs.Tests.Actions;

/// <summary>
/// The confirmation of reference 3.1 and 4. The text tests read the built <c>TASKDIALOGCONFIG</c> through
/// <see cref="ConfirmDialog.Build{T}"/> without showing it; every dialog that is shown is shown on <see cref="HiddenDesktop"/> and
/// answered by <see cref="TaskDialogDriver"/>.
/// </summary>
public unsafe class ConfirmDialogTests
{
    const int IDCANCEL = 2;

    sealed record Config(
        uint Size, uint Flags, uint CommonButtons, string? Title, nint MainIcon, string? Main, string? Content,
        uint ButtonCount, int ButtonId, string? ButtonText, int DefaultButton, string? Verification, bool HasCallback);

    static string? Text(char* s) => s == null ? null : new string(s);

    static Config Read(ConfirmKind kind, int count, string? name, bool critical = false, string? criticalName = null) =>
        ConfirmDialog.Build(0, kind, count, name, critical, criticalName, null, (in ComCtl32.TaskDialogConfig c) => new Config(
            c.CbSize, c.Flags, c.CommonButtons, Text(c.WindowTitle), c.MainIcon, Text(c.MainInstruction), Text(c.Content),
            c.ButtonCount, c.Buttons[0].Id, Text(c.Buttons[0].Text), c.DefaultButton, Text(c.VerificationText), c.Callback != null));

    /// <summary>The parts every confirmation shares (reference 3.1).</summary>
    static void AssertCommon(Config c)
    {
        Assert.Equal(0xA0u, c.Size);
        Assert.Equal(0u, c.Flags);
        Assert.Equal(ComCtl32.TdcbfCancelButton, c.CommonButtons);
        Assert.Equal("Task Manager", c.Title);
        Assert.Equal(ActionStrings.TaskManager, c.Title);
        Assert.Equal(0, c.MainIcon);
        Assert.Equal(1u, c.ButtonCount);
        Assert.Equal(1, c.ButtonId);
        Assert.Equal(1, c.DefaultButton);
    }

    [Fact]
    public void Config_layout_matches_the_x64_header()
    {
        Assert.Equal(0xA0, sizeof(ComCtl32.TaskDialogConfig));
        Assert.Equal(12, sizeof(ComCtl32.TaskDialogButton));
        Assert.Equal(92, (int)System.Runtime.InteropServices.Marshal.OffsetOf<ComCtl32.TaskDialogConfig>(nameof(ComCtl32.TaskDialogConfig.VerificationText)));
        Assert.Equal(140, (int)System.Runtime.InteropServices.Marshal.OffsetOf<ComCtl32.TaskDialogConfig>(nameof(ComCtl32.TaskDialogConfig.Callback)));
        Assert.Equal(156, (int)System.Runtime.InteropServices.Marshal.OffsetOf<ComCtl32.TaskDialogConfig>(nameof(ComCtl32.TaskDialogConfig.Width)));
    }

    [Fact]
    public void Single_end_task_text()
    {
        var c = Read(ConfirmKind.EndTask, 1, "notepad.exe");
        AssertCommon(c);
        Assert.Equal("Do you want to end notepad.exe?", c.Main);
        Assert.Equal(string.Format(ActionStrings.EndTaskOne, "notepad.exe"), c.Main);
        Assert.Equal(ActionStrings.EndTaskOneContent, c.Content);
        Assert.Equal(ActionStrings.EndProcess, c.ButtonText);
        Assert.Equal("End process", c.ButtonText);
        Assert.Null(c.Verification);
        Assert.False(c.HasCallback);
    }

    [Fact]
    public void Plural_end_task_text()
    {
        var c = Read(ConfirmKind.EndTask, 3, null);
        AssertCommon(c);
        Assert.Equal(ActionStrings.EndTaskSeveral, c.Main);
        Assert.Equal(ActionStrings.EndTaskSeveralContent, c.Content);
        Assert.Equal(ActionStrings.EndProcess, c.ButtonText);
        Assert.Null(c.Verification);
    }

    [Fact]
    public void Single_tree_text()
    {
        var c = Read(ConfirmKind.EndProcessTree, 1, "cmd.exe");
        AssertCommon(c);
        Assert.Equal("Do you want to end the process tree of cmd.exe?", c.Main);
        Assert.Equal(string.Format(ActionStrings.EndTreeOne, "cmd.exe"), c.Main);
        Assert.Equal(ActionStrings.EndTreeOneContent, c.Content);
        Assert.Equal(ActionStrings.EndProcessTree, c.ButtonText);
        Assert.Equal("End process tree", c.ButtonText);
    }

    [Fact]
    public void Plural_tree_text()
    {
        var c = Read(ConfirmKind.EndProcessTree, 2, null);
        AssertCommon(c);
        Assert.Equal(ActionStrings.EndTreeSeveral, c.Main);
        Assert.Equal(ActionStrings.EndTreeSeveralContent, c.Content);
        Assert.Equal(ActionStrings.EndProcessTree, c.ButtonText);
    }

    [Fact]
    public void Priority_text()
    {
        var c = Read(ConfirmKind.SetPriority, 1, "notepad.exe");
        AssertCommon(c);
        Assert.Equal("Do you want to change the priority of 'notepad.exe'?", c.Main);
        Assert.Equal(string.Format(ActionStrings.ChangePriorityOne, "notepad.exe"), c.Main);
        Assert.Equal(ActionStrings.ChangePriorityContent, c.Content);
        Assert.Equal(ActionStrings.ChangePriority, c.ButtonText);
    }

    [Fact]
    public void Critical_uses_the_shutdown_text_and_a_verification_box()
    {
        foreach (var kind in new[] { ConfirmKind.EndTask, ConfirmKind.EndProcessTree })
        {
            var c = Read(kind, 1, "notepad.exe", critical: true, criticalName: "csrss.exe");
            AssertCommon(c);
            Assert.Equal("Do you want to end the system process 'csrss.exe'?", c.Main);
            Assert.Equal(string.Format(ActionStrings.EndCritical, "csrss.exe"), c.Main);
            // Task Manager's grammar slip ("any data hasn't been saved") is kept.
            Assert.Equal("Ending this process will cause Windows to become unusable or shut down, causing you to lose any data hasn't been saved. Are you sure you want to continue?", c.Content);
            Assert.Equal(ActionStrings.EndCriticalContent, c.Content);
            Assert.Equal(ActionStrings.ShutDown, c.ButtonText);
            Assert.Equal("Abandon unsaved data and shut down.", c.Verification);
            Assert.Equal(ActionStrings.AbandonAndShutDown, c.Verification);
            Assert.True(c.HasCallback);
        }
    }

    [Fact]
    public void Confirm_returns_true_only_for_the_custom_button()
    {
        bool custom = false, cancel = true, criticalCustom = false, criticalCancel = true;
        var fired = false;
        HiddenDesktop.Run(() =>
        {
            using var driver = new TaskDialogDriver(pressOnActivate: false);
            bool Show(int button, bool critical = false)
            {
                var confirmed = ConfirmDialog.Show(0, ConfirmKind.EndTask, 1, "notepad.exe", critical, critical ? "csrss.exe" : null,
                    dialog => driver.Created(dialog, button));
                driver.Closed();
                return confirmed;
            }

            custom = Show(ConfirmDialog.CustomButtonId);
            cancel = Show(IDCANCEL);
            // TDM_CLICK_BUTTON presses Shut down even while it is disabled, so the critical dialog's result is read the same way.
            criticalCustom = Show(ConfirmDialog.CustomButtonId, critical: true);
            criticalCancel = Show(IDCANCEL, critical: true);
            fired = driver.WatchdogFired;
        });

        Assert.False(fired, "a dialog ignored its button press");
        Assert.True(custom);
        Assert.False(cancel);
        Assert.True(criticalCustom);
        Assert.False(criticalCancel);
    }

    [Fact]
    public void Critical_button_is_enabled_only_while_the_box_is_checked()
    {
        var found = false;
        bool atStart = true, whenChecked = false, whenUnchecked = true, confirmed = true;
        var fired = false;
        HiddenDesktop.Run(() =>
        {
            using var driver = new TaskDialogDriver(pressOnActivate: false);
            // comctl32 sends TDN_VERIFICATION_CLICKED only for a user's click: neither TDM_CLICK_VERIFICATION nor the box's MSAA default
            // action reaches the callback on the hidden desktop. So the dialog is shown as Show shows it, and the test hands its
            // callback the notification a click would send, then reads the real button's state.
            nint callback = 0, data = 0;
            void Verification(nint dialog, bool isChecked) =>
                ((delegate* unmanaged<nint, uint, nint, nint, nint, int>)callback)(dialog, ComCtl32.TdnVerificationClicked, isChecked ? 1 : 0, 0, data);

            confirmed = ConfirmDialog.Build(0, ConfirmKind.EndTask, 1, null, true, "csrss.exe", dialog =>
            {
                var button = TaskDialogDriver.FindDescendant(dialog, "Button", ActionStrings.ShutDown);
                found = button != 0;
                atStart = TaskDialogDriver.IsWindowEnabled(button);
                Verification(dialog, true);
                whenChecked = TaskDialogDriver.IsWindowEnabled(button);
                Verification(dialog, false);
                whenUnchecked = TaskDialogDriver.IsWindowEnabled(button);
                driver.Created(dialog, IDCANCEL);
            }, (in ComCtl32.TaskDialogConfig c) =>
            {
                callback = (nint)c.Callback;
                data = c.CallbackData;
                return ComCtl32.TaskDialogIndirect(c, out var pressed, null, null) >= 0 && pressed == ConfirmDialog.CustomButtonId;
            });
            driver.Closed();
            fired = driver.WatchdogFired;
        });

        Assert.False(fired, "the dialog ignored Cancel");
        Assert.True(found, "no Shut down button");
        Assert.False(atStart);
        Assert.True(whenChecked);
        Assert.False(whenUnchecked);
        Assert.False(confirmed);
    }

    [Fact]
    public void Thousand_open_and_cancel_stay_flat()
    {
        (int Gdi, int User, int Handles) first = default, last = default;
        var fired = false;
        HiddenDesktop.Run(() =>
        {
            using var driver = new TaskDialogDriver(pressOnActivate: false);
            for (var i = 0; i < 1000; i++)
            {
                // Alternate the plain and the critical dialog, so the callback's path is counted too.
                var critical = i % 2 == 1;
                var confirmed = ConfirmDialog.Show(0, ConfirmKind.EndProcessTree, 1, "cmd.exe", critical, critical ? "csrss.exe" : null,
                    dialog => driver.Created(dialog, IDCANCEL));
                driver.Closed();
                Assert.False(confirmed);
                if (i == 0) first = TaskDialogDriver.Counts();
            }
            last = TaskDialogDriver.Counts();
            fired = driver.WatchdogFired;
        });

        Assert.False(fired, "a dialog ignored Cancel");
        TaskDialogDriver.AssertFlat(first, last);
    }
}
