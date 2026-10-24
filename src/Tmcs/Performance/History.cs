namespace Tmcs.Performance;

/// <summary>
/// Sixty seconds of time-stamped points for one device: one shared time column and <see cref="SeriesCount"/> value
/// columns, in fixed rings that never grow, so sampling allocates nothing.
/// </summary>
internal sealed class History
{
    /// <summary>60 s at High speed (0.5 s), plus the point at the left edge.</summary>
    public const int Capacity = 121;

    /// <summary>59 s. Task Manager spreads 60 samples border to border, so at Normal speed this puts every point where it draws it.</summary>
    public const long Window100Ns = 590_000_000;

    readonly long[] _times = new long[Capacity];
    readonly float[] _values;
    int _head;      // ring slot of the oldest point
    long _added;    // points ever accepted

    public History(int seriesCount)
    {
        SeriesCount = seriesCount;
        _values = new float[seriesCount * Capacity];
    }

    public int SeriesCount { get; }
    public int Count { get; private set; }

    /// <summary>Time of the newest point in 100 ns units; 0 when empty.</summary>
    public long LatestTime => Count == 0 ? 0 : _times[Slot(Count - 1)];

    /// <summary>
    /// Index of the newest point at or before <c>LatestTime - Window100Ns</c>, or 0 if there is none, so a graph's
    /// line enters at the left edge.
    /// </summary>
    public int FirstVisible
    {
        get
        {
            if (Count == 0) return 0;
            var edge = LatestTime - Window100Ns;
            for (var i = Count - 1; i >= 0; i--)
                if (TimeAt(i) <= edge) return i;
            return 0;
        }
    }

    public void Add(long time100Ns, ReadOnlySpan<float> values)
    {
        if (values.Length != SeriesCount)
            throw new ArgumentException($"Expected {SeriesCount} values, got {values.Length}.", nameof(values));
        if (Count > 0 && time100Ns <= LatestTime) return;

        int slot;
        if (Count < Capacity) slot = Slot(Count++);
        else { slot = _head; _head = (_head + 1) % Capacity; }

        _times[slot] = time100Ns;
        for (var s = 0; s < SeriesCount; s++) _values[s * Capacity + slot] = values[s];
        _added++;
    }

    /// <summary>Index 0 is the oldest point kept.</summary>
    public long TimeAt(int index) => _times[Slot(index)];

    public float ValueAt(int series, int index) => _values[series * Capacity + Slot(index)];

    /// <summary>How many points were added before this one. Grid lines sit on every n-th point by this number, so they scroll with the data.</summary>
    public long SequenceAt(int index) => _added - Count + index;

    /// <summary>The largest value from <see cref="FirstVisible"/> on; 0 when empty.</summary>
    public float MaxVisible(int series)
    {
        var max = 0f;
        for (var i = FirstVisible; i < Count; i++) max = Math.Max(max, ValueAt(series, i));
        return max;
    }

    int Slot(int index) => (_head + index) % Capacity;
}
