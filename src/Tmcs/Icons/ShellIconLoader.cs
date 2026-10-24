using System.Runtime.InteropServices;
using Tmcs.Native;

namespace Tmcs.Icons;

/// <summary>
/// Loads icons through the shell, without touching its system image list: that list belongs to the process, adds every newly
/// seen icon, and never shrinks. Must be called on an STA thread.
/// </summary>
internal sealed unsafe class ShellIconLoader : IIconLoader
{
    /// <returns>
    /// The file's first icon at the system's small icon size, or 0 for a file with no icon or one that cannot be read, which the
    /// caller shows with the generic icon.
    /// </returns>
    public nint LoadSmallIcon(string path)
    {
        nint small = 0;
        Shell32.ExtractIconEx(path, 0, null, &small, 1); // the handle, not the count, says whether there is an icon to destroy
        return small;
    }

    public void DestroyIcon(nint hIcon) => User32.DestroyIcon(hIcon);

    /// <returns>The generic application icon, or 0 on failure. The caller must destroy it.</returns>
    public static nint LoadGenericIcon()
    {
        var info = new Shell32.ShStockIconInfo { Size = (uint)Marshal.SizeOf<Shell32.ShStockIconInfo>() };
        var hr = Shell32.ShGetStockIconInfo(Shell32.SiidApplication, Shell32.ShgsiIcon | Shell32.ShgsiSmallIcon, ref info);
        return hr < 0 ? 0 : info.Icon;
    }
}
