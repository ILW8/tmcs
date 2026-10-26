using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class Shell32
{
    public const uint ShgsiIcon = 0x100;
    public const uint ShgsiSmallIcon = 0x1;

    /// <summary><c>SIID_APPLICATION</c>: the generic icon of an executable without one of its own.</summary>
    public const int SiidApplication = 2;

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

    /// <returns>An HRESULT. With <see cref="ShgsiIcon"/> the caller must destroy <see cref="ShStockIconInfo.Icon"/>.</returns>
    [LibraryImport("shell32.dll", EntryPoint = "SHGetStockIconInfo")]
    public static partial int ShGetStockIconInfo(int stockIconId, uint flags, ref ShStockIconInfo info);

    /// <summary>
    /// Extracts icons from an exe, DLL or icon file, at the system's large and small icon sizes. Unlike
    /// <c>SHGetFileInfo(SHGFI_ICON)</c> it adds nothing to the process's shell system image list.
    /// </summary>
    /// <param name="file">The file to extract from.</param>
    /// <param name="index">The zero-based index of the first icon to extract.</param>
    /// <param name="largeIcons">Receives large icons, or null for none.</param>
    /// <param name="smallIcons">Receives small icons, or null for none.</param>
    /// <param name="count">How many icons to extract into each array that is not null.</param>
    /// <returns>
    /// The number of icons extracted, or <c>uint.MaxValue</c> when the file cannot be opened. A handle written to the arrays is
    /// the caller's to destroy; a slot left 0 got no icon.
    /// </returns>
    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ExtractIconEx(string file, int index, nint* largeIcons, nint* smallIcons, uint count);

    /// <summary><c>SHOP_FILEPATH</c>: the object name given to <see cref="ShObjectProperties"/> is a file system path.</summary>
    public const uint ShopFilePath = 2;

    /// <summary>Makes the shell's item ID list for a path.</summary>
    /// <returns>The list, which the caller frees with <see cref="IlFree"/>, or 0 when the path does not name anything the shell can find.</returns>
    [LibraryImport("shell32.dll", EntryPoint = "ILCreateFromPathW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint IlCreateFromPath(string path);

    /// <summary>Frees a list made by <see cref="IlCreateFromPath"/>.</summary>
    [LibraryImport("shell32.dll", EntryPoint = "ILFree")]
    public static partial void IlFree(nint pidl);

    /// <summary>Opens an Explorer window on the folder of an item and selects the item. With no children, <paramref name="pidl"/> is the item itself.</summary>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("shell32.dll", EntryPoint = "SHOpenFolderAndSelectItems")]
    public static partial int ShOpenFolderAndSelectItems(nint pidl, uint count, nint* children, uint flags);

    /// <summary>Shows the shell's Properties window for an object.</summary>
    /// <param name="owner">The window that owns the Properties window.</param>
    /// <param name="objectType">What <paramref name="objectName"/> names, such as <see cref="ShopFilePath"/>.</param>
    /// <param name="objectName">The object's name.</param>
    /// <param name="page">The page to open on, or null for the first.</param>
    /// <returns>True when the window was shown.</returns>
    [LibraryImport("shell32.dll", EntryPoint = "SHObjectProperties", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShObjectProperties(nint owner, uint objectType, string objectName, string? page);
}
