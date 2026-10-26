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

    static Config Read(ConfirmKind kind, int count, string? name) =>
        ConfirmDialog.Build(0, kind, count, name, null, (in c) => new Config(
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
    public void Confirm_returns_true_only_for_the_custom_button()
    {
        bool custom = false, cancel = true;
        var fired = false;
        HiddenDesktop.Run(() =>
        {
            using var driver = new TaskDialogDriver(pressOnActivate: false);
            bool Show(int button)
            {
                var confirmed = ConfirmDialog.Show(0, ConfirmKind.EndTask, 1, "notepad.exe", dialog => driver.Created(dialog, button));
                driver.Closed();
                return confirmed;
            }

            custom = Show(ConfirmDialog.CustomButtonId);
            cancel = Show(IDCANCEL);
            fired = driver.WatchdogFired;
        });

        Assert.False(fired, "a dialog ignored its button press");
        Assert.True(custom);
        Assert.False(cancel);
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
                // The hook keeps the callback's path in the count.
                var confirmed = ConfirmDialog.Show(0, ConfirmKind.EndProcessTree, 1, "cmd.exe", dialog => driver.Created(dialog, IDCANCEL));
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
