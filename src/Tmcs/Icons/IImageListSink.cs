namespace Tmcs.Icons;

/// <summary>An image list that copies the icons it is given, so the caller keeps ownership of the handles.</summary>
internal interface IImageListSink
{
    int Count { get; }

    /// <returns>The index of the new image, or -1 on failure.</returns>
    int Add(nint hIcon);

    void Replace(int index, nint hIcon);
}
