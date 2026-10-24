using Tmcs.Native;

namespace Tmcs.Icons;

/// <summary>A color image list of <c>iconSize</c> square images, destroyed by <see cref="Dispose"/>.</summary>
internal sealed class NativeImageList : IImageListSink, IDisposable
{
    /// <summary>0 once disposed.</summary>
    public nint Handle { get; private set; }

    public NativeImageList(int iconSize)
    {
        Handle = ComCtl32.ImageList_Create(iconSize, iconSize, ComCtl32.IlcColor32 | ComCtl32.IlcMask, 64, 32);
        if (Handle == 0) throw new InvalidOperationException("ImageList_Create failed.");
    }

    public int Count => ComCtl32.ImageList_GetImageCount(Handle);

    public int Add(nint hIcon) => ComCtl32.ImageList_ReplaceIcon(Handle, -1, hIcon);

    public void Replace(int index, nint hIcon) => ComCtl32.ImageList_ReplaceIcon(Handle, index, hIcon);

    public void Dispose()
    {
        if (Handle == 0) return;
        ComCtl32.ImageList_Destroy(Handle);
        Handle = 0;
    }
}
