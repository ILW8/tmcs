using System.Runtime.InteropServices;
using Tmcs.Native;

namespace Tmcs.Actions;

/// <summary>The commands that ask before they act.</summary>
internal enum ConfirmKind { EndTask, EndProcessTree, SetPriority }

/// <summary>
/// Task Manager's confirmation (reference 3.1 and 4): a <c>TaskDialogIndirect</c> with Cancel and one custom button, which is the
/// default.
/// </summary>
internal static unsafe class ConfirmDialog
{
    /// <summary>The custom button's id. Pressing it is the only way to confirm.</summary>
    public const int CustomButtonId = 1;

    /// <summary>Asks before <paramref name="kind"/> acts on <paramref name="count"/> rows.</summary>
    /// <param name="owner">The window the dialog is modal to, or null for none.</param>
    /// <param name="kind">The command asking.</param>
    /// <param name="count">How many rows it acts on.</param>
    /// <param name="singleName">The one row's name, used when <paramref name="count"/> is 1.</param>
    /// <returns>True only when the custom button was pressed.</returns>
    public static bool Show(IWin32Window? owner, ConfirmKind kind, int count, string? singleName) =>
        Show(owner?.Handle ?? 0, kind, count, singleName, onCreated: null);

    /// <summary>The public <c>Show</c>, taking the owner as a handle.</summary>
    /// <param name="owner">The owner window's handle, or 0 for none.</param>
    /// <param name="kind">As in the public <c>Show</c>.</param>
    /// <param name="count">As in the public <c>Show</c>.</param>
    /// <param name="singleName">As in the public <c>Show</c>.</param>
    /// <param name="onCreated">
    /// Tests only: runs with the dialog's window on <c>TDN_CREATED</c>, where a test answers the dialog. It must not throw.
    /// </param>
    internal static bool Show(nint owner, ConfirmKind kind, int count, string? singleName, Action<nint>? onCreated) =>
        Build(owner, kind, count, singleName, onCreated, static (in config) =>
            // A dialog that failed to show confirms nothing.
            ComCtl32.TaskDialogIndirect(config, out var button, null, null) >= 0 && button == CustomButtonId);

    internal delegate T ConfigUse<out T>(in ComCtl32.TaskDialogConfig config);

    /// <summary>
    /// Builds the dialog's <c>TASKDIALOGCONFIG</c> and hands it to <paramref name="use"/>. Its strings and button live only until
    /// <paramref name="use"/> returns. Tests read the config here without showing it.
    /// </summary>
    internal static T Build<T>(nint owner, ConfirmKind kind, int count, string? singleName, Action<nint>? onCreated, ConfigUse<T> use)
    {
        var (main, content, buttonText) = Text(kind, count, singleName);

        // The callback is there for a test's hook only; the dialog has none otherwise.
        var state = onCreated is not null ? GCHandle.Alloc(onCreated) : default;
        try
        {
            fixed (char* title = ActionStrings.TaskManager, pMain = main, pContent = content, pButton = buttonText)
            {
                var button = new ComCtl32.TaskDialogButton { Id = CustomButtonId, Text = pButton };
                var config = new ComCtl32.TaskDialogConfig
                {
                    CbSize = (uint)sizeof(ComCtl32.TaskDialogConfig),
                    Owner = owner,
                    Flags = 0,
                    CommonButtons = ComCtl32.TdcbfCancelButton,
                    WindowTitle = title,
                    MainInstruction = pMain,
                    Content = pContent,
                    ButtonCount = 1,
                    Buttons = &button,
                    DefaultButton = CustomButtonId,
                    Callback = state.IsAllocated ? &Callback : null,
                    CallbackData = state.IsAllocated ? GCHandle.ToIntPtr(state) : 0,
                };
                return use(in config);
            }
        }
        finally
        {
            if (state.IsAllocated) state.Free();
        }
    }

    static (string Main, string Content, string Button) Text(ConfirmKind kind, int count, string? singleName) => kind switch
    {
        ConfirmKind.EndTask when count > 1 => (ActionStrings.EndTaskSeveral, ActionStrings.EndTaskSeveralContent, ActionStrings.EndProcess),
        ConfirmKind.EndTask => (string.Format(ActionStrings.EndTaskOne, singleName), ActionStrings.EndTaskOneContent, ActionStrings.EndProcess),
        ConfirmKind.EndProcessTree when count > 1 => (ActionStrings.EndTreeSeveral, ActionStrings.EndTreeSeveralContent, ActionStrings.EndProcessTree),
        ConfirmKind.EndProcessTree => (string.Format(ActionStrings.EndTreeOne, singleName), ActionStrings.EndTreeOneContent, ActionStrings.EndProcessTree),
        // Set priority acts on one row only.
        ConfirmKind.SetPriority => (string.Format(ActionStrings.ChangePriorityOne, singleName), ActionStrings.ChangePriorityContent, ActionStrings.ChangePriority),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The <c>PFTASKDIALOGCALLBACK</c>. <paramref name="refData"/> is the GCHandle of the <c>onCreated</c> hook.</summary>
    [UnmanagedCallersOnly]
    static int Callback(nint hwnd, uint notification, nint wParam, nint lParam, nint refData)
    {
        try
        {
            if (notification == ComCtl32.TdnCreated) ((Action<nint>)GCHandle.FromIntPtr(refData).Target!)(hwnd);
        }
        catch
        {
            // Nothing may escape into comctl32. Only a test's hook can throw here.
        }
        return 0; // S_OK
    }
}
