using System.Runtime.InteropServices;
using Tmcs.Native;

namespace Tmcs.Icons;

/// <summary>Loads icons through the shell. Must be called on an STA thread.</summary>
internal sealed class ShellIconLoader : IIconLoader
{
    public nint LoadSmallIcon(string path)
    {
        var info = new Shell32.ShFileInfo();
        var result = Shell32.ShGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<Shell32.ShFileInfo>(), Shell32.ShgfiIcon | Shell32.ShgfiSmallIcon);
        return result == 0 ? 0 : info.Icon;
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
