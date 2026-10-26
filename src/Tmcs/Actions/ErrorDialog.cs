using Tmcs.Native;

namespace Tmcs.Actions;

/// <summary>The commands whose failure has its own error title.</summary>
internal enum ErrorKind { EndTask, EndProcessTree, SetPriority, Affinity }

/// <summary>
/// Task Manager's error dialog (reference 3.3): a <c>TaskDialog</c> with OK and the error icon, titled by the command, worded by the
/// error.
/// </summary>
internal static unsafe class ErrorDialog
{
    /// <summary>Shows why <paramref name="kind"/> failed with <paramref name="outcome"/>.</summary>
    /// <param name="owner">The window the dialog is modal to, or null for none.</param>
    /// <param name="kind">The command that failed.</param>
    /// <param name="outcome">Its outcome, which words the dialog.</param>
    /// <param name="isElevated">tmcs runs elevated: access denied then reads as the system's message, not "run it as an administrator".</param>
    public static void Show(IWin32Window? owner, ErrorKind kind, ActionOutcome outcome, bool isElevated)
    {
        var (main, content) = Text(outcome, isElevated);
        ShowCore(owner, Title(kind), main, content);
    }

    /// <summary>
    /// The same dialog for a notice that is not an <see cref="ActionOutcome"/> failure: Set priority's "set to High instead". The body
    /// is the main instruction, with no content.
    /// </summary>
    public static void ShowNotice(IWin32Window? owner, string title, string body) => ShowCore(owner, title, body, null);

    static void ShowCore(IWin32Window? owner, string title, string main, string? content) =>
        // Nothing waits on the answer, and a dialog that failed to show has nothing to retry.
        ComCtl32.TaskDialog(owner?.Handle ?? 0, 0, title, main, content, ComCtl32.TdcbfOkButton, ComCtl32.TdErrorIcon, out _);

    internal static string Title(ErrorKind kind) => kind switch
    {
        ErrorKind.EndTask => ActionStrings.EndTaskFailedTitle,
        ErrorKind.EndProcessTree => ActionStrings.EndTreeFailedTitle,
        ErrorKind.SetPriority => ActionStrings.PriorityFailedTitle,
        ErrorKind.Affinity => ActionStrings.AffinityFailedTitle,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The main instruction and content, by the rows of reference 3.3.</summary>
    internal static (string MainInstruction, string Content) Text(ActionOutcome outcome, bool isElevated)
    {
        if (!isElevated && outcome.IsAccessDenied) return (ActionStrings.AccessDenied, ActionStrings.NeedAdministrator);
        if (outcome.IsInvalidArg) return (ActionStrings.OperationFailed, ActionStrings.NotValidForProcess);
        return (ActionStrings.OperationFailed, SystemMessage(outcome.HResult));
    }

    /// <summary>
    /// The system's text for <paramref name="hresult"/>, as <c>FormatMessage</c> gives it, without the trailing line break. A code
    /// the system has no text for shows as itself, for example <c>0x8004DEAD</c>.
    /// </summary>
    internal static string SystemMessage(int hresult)
    {
        const int capacity = 1024;
        var buffer = stackalloc char[capacity];
        var length = Kernel32.FormatMessageW(Kernel32.FormatMessageFromSystem | Kernel32.FormatMessageIgnoreInserts, 0, (uint)hresult, 0, buffer, capacity, 0);
        var text = new string(buffer, 0, (int)length).Trim();
        return text.Length > 0 ? text : $"0x{hresult:X8}";
    }
}
