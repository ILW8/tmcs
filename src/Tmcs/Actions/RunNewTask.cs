using System.Diagnostics;
using System.Runtime.InteropServices;
using Tmcs.Native;

namespace Tmcs.Actions;

/// <summary>
/// File ▸ Run new task (reference 2): the shell's own Run dialog, called as Task Manager calls it, on a thread of its own. When its
/// admin box is checked, or it has none, the shell starts the command itself. Otherwise it sends the owner a <c>WM_NOTIFY</c> with
/// <see cref="RunNotifyCode"/>, and <see cref="HandleRunNotify"/> starts the command and tells the dialog whether to close.
/// </summary>
internal static unsafe class RunNewTask
{
    /// <summary>The code of the Run dialog's <c>WM_NOTIFY</c> asking its owner to start a command (reference 2.4).</summary>
    public const int RunNotifyCode = -511;

    /// <summary>Task Manager's base flags (reference 2.2).</summary>
    private const uint BaseFlags = 0x14;

    /// <summary>No Browse button.</summary>
    private const uint NoBrowse = 0x01;

    /// <summary>The dialog template with the "Create this task with administrative privileges." box.</summary>
    private const uint AdminCheckbox = 0x40;

    /// <summary>The answer that closes the dialog: the command was handled.</summary>
    private const int Handled = 1;

    /// <summary>The answer that keeps the dialog open. Task Manager shows no error then.</summary>
    private const int KeepOpen = 2;

    /// <summary>What File ▸ Run new task calls with the main window and its icon. Tests replace it, so no test shows the dialog.</summary>
    internal static Action<nint, nint> ShowDialog { get; set; } = Show;

    /// <summary>
    /// What <see cref="HandleRunNotify"/> starts a command with: the command, the directory (or null) and the DWORD the dialog sent
    /// with them. It returns whether the command started. Tests replace it, so no test starts anything.
    /// </summary>
    internal static Func<string, string?, uint, bool> Launcher { get; set; } = Launch;

    /// <summary>
    /// Shows the Run dialog owned by <paramref name="owner"/>, with a copy of <paramref name="icon"/>, and returns at once: the dialog
    /// runs on a new single-threaded-apartment thread, as Task Manager's does, and the UI thread keeps running while it is open. The
    /// dialog owns the copy, so the caller's icon may be destroyed while the dialog is still open.
    /// </summary>
    public static void Show(nint owner, nint icon)
    {
        var copy = icon == 0 ? 0 : User32.CopyIcon(icon);
        var args = DialogArgs.Create(owner, copy, Environment.CurrentDirectory, CurrentFlags());
        if (!ShlwApi.ShCreateThread(&DialogThread, args, ShlwApi.CtfCoInitSta, null)) DialogArgs.Free(args);
    }

    /// <summary>
    /// The Run dialog's flags (reference 2.2): Task Manager's base 0x14; the admin box when the system is not in Safe Mode, wdc.dll
    /// can start a task as the user, and the process is not WOW64; and no Browse button when ShellStyle.dll is missing. Task
    /// Manager's 0x80 is never set.
    /// </summary>
    internal static uint ComputeFlags(bool safeMode, bool wdcAvailable, bool wow64, bool shellStyleExists)
    {
        var flags = BaseFlags;
        if (!safeMode && wdcAvailable && !wow64) flags |= AdminCheckbox;
        if (!shellStyleExists) flags |= NoBrowse;
        return flags;
    }

    /// <summary>
    /// Answers the Run dialog's <c>WM_NOTIFY</c> <see cref="RunNotifyCode"/>. <paramref name="lParam"/> holds, after the
    /// <c>NMHDR</c>, the command at 0x18, the working directory at 0x20 and a DWORD at 0x2c (reference 2.4; static offsets, the
    /// payload probe was not run). Starts the command through <see cref="Launcher"/>.
    /// </summary>
    /// <returns>1 when the command started, which closes the dialog; 2 when it did not, which keeps the dialog open. It never throws.</returns>
    public static int HandleRunNotify(nint lParam)
    {
        try
        {
            if (lParam == 0) return KeepOpen;
            var payload = (byte*)lParam;
            var command = Marshal.PtrToStringUni(*(nint*)(payload + 0x18));
            if (command is null) return KeepOpen;
            var directory = Marshal.PtrToStringUni(*(nint*)(payload + 0x20));
            var flags = *(uint*)(payload + 0x2c);
            return Launcher(command, directory, flags) ? Handled : KeepOpen;
        }
        catch (Exception e)
        {
            // This runs inside the dialog's SendMessage: an exception must not unwind into the shell.
            Debug.WriteLine(e);
            return KeepOpen;
        }
    }

    /// <summary>Whether <paramref name="lParam"/> of a <c>WM_NOTIFY</c> is the Run dialog's request to start a command.</summary>
    internal static bool IsRunNotify(nint lParam) => lParam != 0 && ((User32.NmHdr*)lParam)->Code == RunNotifyCode;

    /// <summary>
    /// The real launcher. The dialog's DWORD is not passed on (ruling R12): wdc.dll always gets flags 0. Without wdc.dll it starts
    /// nothing and returns false, which keeps the dialog open; the request never comes then anyway, because the dialog has no admin
    /// box without wdc.dll (<see cref="ComputeFlags"/>), and without the box the shell starts the command itself.
    /// </summary>
    private static bool Launch(string command, string? directory, uint dialogFlags) =>
        WdcLauncher.IsAvailable && WdcLauncher.Run(command, directory) >= 0;

    private static uint CurrentFlags()
    {
        var safeMode = User32.GetSystemMetrics(User32.SmCleanBoot) != 0;
        var wow64 = Kernel32.IsWow64Process(Kernel32.GetCurrentProcess(), out var isWow64) && isWow64;
        var shellStyle = File.Exists(Path.Combine(Environment.SystemDirectory, "ShellStyle.dll"));
        return ComputeFlags(safeMode, WdcLauncher.IsAvailable, wow64, shellStyle);
    }

    [UnmanagedCallersOnly]
    private static uint DialogThread(void* data)
    {
        try
        {
            var args = (DialogArgs*)data;
            fixed (char* title = ActionStrings.CreateNewTask)
            fixed (char* prompt = ActionStrings.CreateNewTaskPrompt)
                Shell32.RunFileDlg(args->Owner, args->Icon, args->Directory, title, prompt, args->Flags);
        }
        catch
        {
            // An exception must not unwind into native code.
        }
        finally
        {
            DialogArgs.Free(data);
        }

        return 0;
    }

    /// <summary>
    /// What the dialog thread needs, in one native block that outlives <see cref="Show"/>: the dialog thread frees it once the
    /// dialog has closed, or <see cref="Show"/> does if the thread could not start. The directory follows the fields, zero-terminated.
    /// <see cref="Icon"/> is the dialog's own copy, destroyed with the block.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DialogArgs
    {
        public nint Owner;
        public nint Icon;
        public char* Directory;
        public uint Flags;

        public static DialogArgs* Create(nint owner, nint icon, string directory, uint flags)
        {
            var size = (nuint)sizeof(DialogArgs) + (nuint)(directory.Length + 1) * sizeof(char);
            var args = (DialogArgs*)NativeMemory.Alloc(size);
            var text = (char*)(args + 1);
            directory.AsSpan().CopyTo(new Span<char>(text, directory.Length));
            text[directory.Length] = '\0';
            *args = new DialogArgs { Owner = owner, Icon = icon, Directory = text, Flags = flags };
            return args;
        }

        /// <summary>Destroys the icon copy, then frees the block.</summary>
        public static void Free(void* args)
        {
            var icon = ((DialogArgs*)args)->Icon;
            if (icon != 0) User32.DestroyIcon(icon);
            NativeMemory.Free(args);
        }
    }
}
