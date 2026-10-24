namespace Tmcs.Icons;

/// <summary>
/// Maps an image path to a slot in an image list, so the list holds one image per distinct path instead of one per process.
/// A path nobody holds any more stays cached in a bounded pool, and the slot of the path released longest ago is reused by the
/// next new path. Every icon loaded here is destroyed straight after the image list has copied it. Not thread-safe.
/// </summary>
internal sealed class IconStore
{
    /// <summary>Holds the generic icon. It is shown for paths without an icon of their own and is never reused.</summary>
    public const int GenericSlot = 0;

    private sealed class Entry(int slot, LinkedListNode<string> poolNode)
    {
        public int Slot { get; } = slot;
        public int RefCount { get; set; }

        /// <summary>Linked into <c>_pool</c> exactly while <see cref="RefCount"/> is 0.</summary>
        public LinkedListNode<string> PoolNode { get; } = poolNode;
    }

    private readonly IImageListSink _sink;
    private readonly IIconLoader _loader;
    private readonly int _poolCapacity;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Entries with a reference count of 0, most recently released first.</summary>
    private readonly LinkedList<string> _pool = new();

    private readonly Stack<int> _freeSlots = new();

    /// <param name="sink">The image list the slots index into. It must be empty.</param>
    /// <param name="loader">Loads the icon of a path. Every handle it returns is destroyed through it.</param>
    /// <param name="genericIcon">Added to the empty <paramref name="sink"/> as <see cref="GenericSlot"/>. The caller keeps ownership of the handle.</param>
    /// <param name="releasedPoolCapacity">How many paths nobody holds are kept cached before the oldest is dropped.</param>
    public IconStore(IImageListSink sink, IIconLoader loader, nint genericIcon, int releasedPoolCapacity = 64)
    {
        _sink = sink;
        _loader = loader;
        _poolCapacity = releasedPoolCapacity;
        if (sink.Add(genericIcon) != GenericSlot)
            throw new InvalidOperationException("The image list must be empty, and the generic icon must be addable to it.");
    }

    /// <summary>Distinct paths that are held at the moment.</summary>
    public int LiveCount => _entries.Count - _pool.Count;

    /// <summary>Paths that nobody holds but whose slot is still cached.</summary>
    public int PooledCount => _pool.Count;

    public int SlotCount => _sink.Count;

    /// <returns>The slot to show for <paramref name="imagePath"/>: <see cref="GenericSlot"/> for a null or empty path or one without an icon.</returns>
    public int Acquire(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return GenericSlot;

        if (_entries.TryGetValue(imagePath, out var entry))
        {
            if (entry.RefCount == 0) _pool.Remove(entry.PoolNode);
            entry.RefCount++;
            return entry.Slot;
        }

        entry = new Entry(LoadIntoSlot(imagePath), new LinkedListNode<string>(imagePath)) { RefCount = 1 };
        _entries.Add(imagePath, entry);
        return entry.Slot;
    }

    /// <summary>Gives back one <see cref="Acquire"/>. Ignores a path that is unknown or already fully released.</summary>
    public void Release(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !_entries.TryGetValue(imagePath, out var entry) || entry.RefCount == 0) return;
        if (--entry.RefCount > 0) return;

        _pool.AddFirst(entry.PoolNode);
        if (_pool.Count > _poolCapacity) EvictOldest();
    }

    private int LoadIntoSlot(string imagePath)
    {
        var icon = _loader.LoadSmallIcon(imagePath);
        if (icon == 0) return GenericSlot;

        try
        {
            if (_freeSlots.TryPop(out var free))
            {
                _sink.Replace(free, icon);
                return free;
            }

            var added = _sink.Add(icon);
            return added < 0 ? GenericSlot : added;
        }
        finally
        {
            _loader.DestroyIcon(icon);
        }
    }

    private void EvictOldest()
    {
        var oldest = _pool.Last!;
        _pool.RemoveLast();
        _entries.Remove(oldest.Value, out var entry);
        if (entry!.Slot != GenericSlot) _freeSlots.Push(entry.Slot);
    }
}
