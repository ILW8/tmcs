using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static partial class ComCtl32
{
    public const uint IlcMask = 0x1;
    public const uint IlcColor32 = 0x20;

    /// <returns>The image list handle, or 0 on failure. The caller must <see cref="ImageList_Destroy"/> it.</returns>
    [LibraryImport("comctl32.dll")]
    public static partial nint ImageList_Create(int cx, int cy, uint flags, int initial, int grow);

    /// <summary>The image list copies the icon, so the caller still owns <paramref name="icon"/>.</summary>
    /// <param name="index">The image to replace, or -1 to append one.</param>
    /// <returns>The index of the image, or -1 on failure.</returns>
    [LibraryImport("comctl32.dll")]
    public static partial int ImageList_ReplaceIcon(nint imageList, int index, nint icon);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImageList_Destroy(nint imageList);

    [LibraryImport("comctl32.dll")]
    public static partial int ImageList_GetImageCount(nint imageList);
}
