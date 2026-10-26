namespace Tmcs.Actions;

/// <summary>
/// The result of one action on one process. <see cref="HResult"/> is an HRESULT (Win32 errors arrive as <c>HRESULT_FROM_WIN32</c>).
/// A failure the caller must show in the error dialog has <see cref="ShowDialog"/> set; a failure without it is reported to no one.
/// </summary>
internal readonly record struct ActionOutcome(int HResult, bool ShowDialog)
{
    public const int EAccessDenied = unchecked((int)0x80070005);
    public const int EInvalidArg = unchecked((int)0x80070057);
    public const int ENotImpl = unchecked((int)0x80004001);

    public static ActionOutcome Success => new(0, false);

    /// <summary>
    /// The process is gone: it can't be opened because it exited, or its PID now belongs to a process with another creation time.
    /// Shown like any E_INVALIDARG ("The operation is not valid for this process."), except where a caller skips it on purpose
    /// (<see cref="ProcessTree.EndEach"/> for a descendant).
    /// </summary>
    public static ActionOutcome Gone => new(EInvalidArg, ShowDialog: true) { IsGone = true };

    /// <summary>Set only by <see cref="Gone"/>.</summary>
    public bool IsGone { get; init; }

    public bool Ok => HResult >= 0;
    public bool IsAccessDenied => HResult == EAccessDenied;
    public bool IsInvalidArg => HResult == EInvalidArg;

    /// <summary><c>HRESULT_FROM_WIN32</c>: a Win32 error code as an HRESULT in the Win32 facility.</summary>
    public static int FromWin32(int error) => error <= 0 ? error : (int)(((uint)error & 0xFFFF) | 0x80070000);
}

/// <summary>The result of Set priority. <see cref="ReadBackMatched"/> is false when the class read back differs from the one asked for (Realtime without the privilege reads back as High).</summary>
internal readonly record struct PrioritySet(ActionOutcome Outcome, bool ReadBackMatched);

/// <summary>A process's affinity mask and the system's, as <c>GetProcessAffinityMask</c> returns them.</summary>
internal readonly record struct AffinityInfo(nuint ProcessMask, nuint SystemMask);
