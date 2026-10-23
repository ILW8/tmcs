using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class Shell32
{
    public const uint ShgfiIcon = 0x100;
    public const uint ShgfiSmallIcon = 0x1;

    public const uint ShgsiIcon = 0x100;
    public const uint ShgsiSmallIcon = 0x1;

    /// <summary><c>SIID_APPLICATION</c>: the generic icon of an executable without one of its own.</summary>
    public const int SiidApplication = 2;

    /// <summary><c>SHFILEINFOW</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ShFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        public fixed char DisplayName[260];
        public fixed char TypeName[80];
    }

    /// <summary><c>SHSTOCKICONINFO</c>. <see cref="Size"/> must be set before the call.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ShStockIconInfo
    {
        public uint Size;
        public nint Icon;
        public int SysImageIndex;
        public int IconIndex;
        public fixed char Path[260];
    }

    /// <returns>Nonzero on success. With <see cref="ShgfiIcon"/> the caller must destroy <see cref="ShFileInfo.Icon"/>.</returns>
    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nuint ShGetFileInfo(string path, uint fileAttributes, ref ShFileInfo info, uint size, uint flags);

    /// <returns>An HRESULT. With <see cref="ShgsiIcon"/> the caller must destroy <see cref="ShStockIconInfo.Icon"/>.</returns>
    [LibraryImport("shell32.dll", EntryPoint = "SHGetStockIconInfo")]
    public static partial int ShGetStockIconInfo(int stockIconId, uint flags, ref ShStockIconInfo info);
}
