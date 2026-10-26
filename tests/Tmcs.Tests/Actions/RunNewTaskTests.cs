using System.Runtime.InteropServices;
using Tmcs.Actions;

namespace Tmcs.Tests.Actions;

/// <summary>
/// Run new task (reference 2): the Run dialog's flags and the answer to its <c>WM_NOTIFY −511</c>. No test shows the shell's Run
/// dialog or calls <c>wdc.dll</c>: the flags come from a pure function, and every launch goes through <see cref="RunNewTask.Launcher"/>,
/// which each test replaces, so nothing is ever started.
/// </summary>
public class RunNewTaskTests
{
    // Written out rather than taken from production code, so a wrong constant shows up.
    private const uint Base = 0x14;
    private const uint NoBrowse = 0x01;
    private const uint AdminCheckbox = 0x40;

    [Fact]
    public void Flags_add_the_checkbox_when_wdc_is_available()
    {
        Assert.Equal(Base | AdminCheckbox, RunNewTask.ComputeFlags(safeMode: false, wdcAvailable: true, wow64: false, shellStyleExists: true));
        Assert.Equal(Base, RunNewTask.ComputeFlags(safeMode: false, wdcAvailable: false, wow64: false, shellStyleExists: true));
        // Safe Mode and WOW64 each take the checkbox away too.
        Assert.Equal(Base, RunNewTask.ComputeFlags(safeMode: true, wdcAvailable: true, wow64: false, shellStyleExists: true));
        Assert.Equal(Base, RunNewTask.ComputeFlags(safeMode: false, wdcAvailable: true, wow64: true, shellStyleExists: true));
    }

    [Fact]
    public void Flags_add_no_browse_when_shellstyle_is_missing()
    {
        Assert.Equal(Base | NoBrowse, RunNewTask.ComputeFlags(safeMode: false, wdcAvailable: false, wow64: false, shellStyleExists: false));
        Assert.Equal(Base | AdminCheckbox | NoBrowse, RunNewTask.ComputeFlags(safeMode: false, wdcAvailable: true, wow64: false, shellStyleExists: false));
    }

    [Fact]
    public void Flags_never_include_0x80()
    {
        foreach (var safeMode in new[] { false, true })
        foreach (var wdc in new[] { false, true })
        foreach (var wow64 in new[] { false, true })
        foreach (var shellStyle in new[] { false, true })
            Assert.Equal(0u, RunNewTask.ComputeFlags(safeMode, wdc, wow64, shellStyle) & 0x80);
    }

    [Fact]
    public void Notify_reads_the_payload_at_the_reference_offsets()
    {
        var calls = new List<(string Command, string? Directory, uint Flags)>();
        var result = WithLauncher((c, d, f) =>
        {
            calls.Add((c, d, f));
            return true;
        }, () => Notify("notepad.exe notes.txt", @"C:\Work", 0x1234_5678));

        Assert.Equal(1, result);
        Assert.Equal([("notepad.exe notes.txt", @"C:\Work", 0x1234_5678u)], calls);
    }

    [Fact]
    public void Notify_passes_a_missing_directory_as_null()
    {
        var calls = new List<(string Command, string? Directory, uint Flags)>();
        var result = WithLauncher((c, d, f) =>
        {
            calls.Add((c, d, f));
            return true;
        }, () => Notify("calc", null, 0));

        Assert.Equal(1, result);
        Assert.Equal([("calc", null, 0u)], calls);
    }

    [Fact]
    public void Notify_returns_2_when_the_launch_fails()
    {
        Assert.Equal(2, WithLauncher((_, _, _) => false, () => Notify("nosuchprogram", @"C:\", 0)));
    }

    [Fact]
    public void Notify_returns_2_when_the_launcher_throws()
    {
        Assert.Equal(2, WithLauncher((_, _, _) => throw new InvalidOperationException("boom"), () => Notify("x", null, 0)));
    }

    [Fact]
    public void Notify_without_a_command_launches_nothing_and_returns_2()
    {
        var calls = 0;
        Assert.Equal(2, WithLauncher((_, _, _) =>
        {
            calls++;
            return true;
        }, () => Notify(null, @"C:\", 0)));
        Assert.Equal(0, calls);
        Assert.Equal(2, WithLauncher((_, _, _) =>
        {
            calls++;
            return true;
        }, () => RunNewTask.HandleRunNotify(0)));
        Assert.Equal(0, calls);
    }

    /// <summary>Runs <paramref name="body"/> with <paramref name="launcher"/> in place of the real one, then puts the real one back.</summary>
    internal static int WithLauncher(Func<string, string?, uint, bool> launcher, Func<int> body)
    {
        var original = RunNewTask.Launcher;
        try
        {
            RunNewTask.Launcher = launcher;
            return body();
        }
        finally
        {
            RunNewTask.Launcher = original;
        }
    }

    /// <summary>Calls <see cref="RunNewTask.HandleRunNotify"/> with a payload built by <see cref="WithPayload"/>.</summary>
    private static int Notify(string? command, string? directory, uint flags) =>
        WithPayload(command, directory, flags, RunNewTask.HandleRunNotify);

    /// <summary>
    /// Builds the Run dialog's <c>WM_NOTIFY −511</c> payload as the reference gives it (2.4): an <c>NMHDR</c> (code at 0x10), the
    /// command at 0x18, the directory at 0x20, a DWORD that is not read at 0x28, and the flags DWORD at 0x2c. Every byte the
    /// handler must not read holds a marker, so reading the wrong offset shows up.
    /// </summary>
    internal static unsafe int WithPayload(string? command, string? directory, uint flags, Func<nint, int> handle)
    {
        var commandText = command is null ? 0 : Marshal.StringToHGlobalUni(command);
        var directoryText = directory is null ? 0 : Marshal.StringToHGlobalUni(directory);
        var block = (byte*)NativeMemory.Alloc(0x30);
        try
        {
            new Span<byte>(block, 0x30).Fill(0xCD);
            *(nint*)(block + 0x00) = 0; // hwndFrom
            *(nuint*)(block + 0x08) = 0; // idFrom
            *(int*)(block + 0x10) = -511; // code
            *(nint*)(block + 0x18) = commandText;
            *(nint*)(block + 0x20) = directoryText;
            *(uint*)(block + 0x28) = 0xDEAD_BEEF;
            *(uint*)(block + 0x2c) = flags;
            return handle((nint)block);
        }
        finally
        {
            NativeMemory.Free(block);
            if (commandText != 0) Marshal.FreeHGlobal(commandText);
            if (directoryText != 0) Marshal.FreeHGlobal(directoryText);
        }
    }
}
