using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tmcs.Tests.Support;

/// <summary>
/// Keeps the test run silent. A TaskDialog with an icon plays the matching system sound, and a hidden desktop hides windows, not
/// sounds, so the dialog tests (a thousand error dialogs in the leak test) would sound on the owner's speakers. Before any test
/// runs, this redirects <c>winmm!PlaySoundW</c> and <c>user32!MessageBeep</c> to stubs that only count the call. The patch is made
/// to the functions' code in this process, so it covers every caller (either comctl32, loaded now or later) and nothing outside the
/// test host.
/// </summary>
internal static unsafe partial class DialogSounds
{
    private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    private static int _plays, _beeps;

    /// <summary>Sounds the stubs swallowed: <c>PlaySoundW</c> calls and <c>MessageBeep</c> calls.</summary>
    public static (int Plays, int Beeps) Swallowed => (Volatile.Read(ref _plays), Volatile.Read(ref _beeps));

    [ModuleInitializer]
    internal static void Silence()
    {
        Redirect("winmm.dll", "PlaySoundW", (nint)(delegate* unmanaged<nint, nint, uint, int>)&SilentPlaySound);
        Redirect("user32.dll", "MessageBeep", (nint)(delegate* unmanaged<uint, int>)&SilentMessageBeep);
    }

    /// <summary>The first 12 bytes of <paramref name="name"/> become <c>mov rax, stub; jmp rax</c>.</summary>
    private static void Redirect(string dll, string name, nint stub)
    {
        var module = LoadLibraryExW(dll, 0, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (module == 0) throw new Win32Exception();
        var target = (byte*)GetProcAddress(module, name); // follows export forwarders to the code itself
        if (target == null) throw new Win32Exception();
        if (!VirtualProtect(target, 12, PAGE_EXECUTE_READWRITE, out var old)) throw new Win32Exception();
        target[0] = 0x48; target[1] = 0xB8; // mov rax, imm64
        *(nint*)(target + 2) = stub;
        target[10] = 0xFF; target[11] = 0xE0; // jmp rax
        VirtualProtect(target, 12, old, out _);
        FlushInstructionCache(GetCurrentProcess(), target, 12);
    }

    [UnmanagedCallersOnly]
    private static int SilentPlaySound(nint sound, nint module, uint flags)
    {
        Interlocked.Increment(ref _plays);
        return 1; // TRUE: "played"
    }

    [UnmanagedCallersOnly]
    private static int SilentMessageBeep(uint type)
    {
        Interlocked.Increment(ref _beeps);
        return 1;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryExW(string name, nint file, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetProcAddress(nint module, string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualProtect(void* address, nuint size, uint protect, out uint oldProtect);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushInstructionCache(nint process, void* address, nuint size);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();
}
