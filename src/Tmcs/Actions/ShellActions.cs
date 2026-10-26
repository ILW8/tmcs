using System.Runtime.InteropServices;
using Tmcs.Native;

namespace Tmcs.Actions;

/// <summary>
/// The Details row menu's shell commands (reference 6): Open file location, Properties, and the Copy items. Each is a plain call into
/// the shell or the clipboard that reports nothing back: Task Manager shows no error for them either.
/// </summary>
internal static class ShellActions
{
    /// <summary>What <see cref="CopyText"/> writes the clipboard with. Tests replace it, so no test writes the owner's clipboard.</summary>
    internal static Action<string> ClipboardWriter { get; set; } = Clipboard.SetText;

    /// <summary>Opens an Explorer window on the folder of <paramref name="imagePath"/> and selects the file.</summary>
    public static unsafe void OpenFileLocation(string imagePath)
    {
        var pidl = Shell32.IlCreateFromPath(imagePath);
        if (pidl == 0) return; // the shell cannot find the path

        try
        {
            Shell32.ShOpenFolderAndSelectItems(pidl, 0, null, 0);
        }
        finally
        {
            Shell32.IlFree(pidl);
        }
    }

    /// <summary>Shows the shell's Properties window for the file at <paramref name="imagePath"/>, owned by the window <paramref name="owner"/>.</summary>
    public static void ShowProperties(nint owner, string imagePath) =>
        Shell32.ShObjectProperties(owner, Shell32.ShopFilePath, imagePath, null);

    /// <summary>Puts <paramref name="text"/> on the clipboard. Another process may hold the clipboard open, and then nothing is copied.</summary>
    public static void CopyText(string text)
    {
        try
        {
            ClipboardWriter(text);
        }
        catch (ExternalException)
        {
            // The clipboard is locked by another process.
        }
    }
}
