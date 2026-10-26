namespace Tmcs.Performance;

/// <summary>
/// Sixty seconds of time-stamped points for one device: one shared time column and <see cref="SeriesCount"/> value
/// columns, in fixed rings that never grow, so sampling allocates nothing.
/// <para>
/// Points are at least <see cref="MinSpacing100Ns"/> apart: a sample closer than that to the newest point's first sample
/// folds into it. So however fast F5 is pressed, the ring still holds the whole window, and a burst reads as if sampled
/// every 0.4 s rather than at the key's repeat rate.
/// </para>
/// </summary>
internal sealed class History
{
    /// <summary>0.4 s: below High speed's 0.5 s, so its ticks never fold, even 0.1 s early.</summary>
    public const long MinSpacing100Ns = 4_000_000;

    /// <summary>
    /// 149 spacings cover 59.6 s, more than the window. So once the ring is full, the oldest point kept is at or before the
    /// window's left edge, and the line enters at the left border.
    /// </summary>
    public const int Capacity = 151;

    /// <summary>59 s. Task Manager spreads 60 samples border to border, so at Normal speed this puts every point where it draws it.</summary>
    public const long Window100Ns = 590_000_000;

    readonly long[] _times = new long[Capacity];
    readonly float[] _values;
    int _head;      // ring slot of the oldest point
    long _anchor;   // time of the newest point's first sample

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

    /// <summary>
    /// Adds a point after the newest, dropping the oldest when the ring is full. A sample less than
    /// <see cref="MinSpacing100Ns"/> after the newest point's first sample folds into that point instead: it takes the
    /// sample's time, and each value becomes the mean over the point's span and the sample's, weighted by their lengths.
    /// Each value covers the time since the sample before it, so the point reads what one sample over both spans would.
    /// </summary>
    public void Add(long time100Ns, ReadOnlySpan<float> values)
    {
        if (values.Length != SeriesCount)
            throw new ArgumentException($"Expected {SeriesCount} values, got {values.Length}.", nameof(values));
        if (Count > 0 && time100Ns <= LatestTime) return;

        if (Count > 0 && time100Ns - _anchor < MinSpacing100Ns)
        {
            var newest = Slot(Count - 1);
            // The only point has no span of its own, so the sample replaces it.
            double kept = Count > 1 ? _times[newest] - TimeAt(Count - 2) : 0, added = time100Ns - _times[newest];
            for (var s = 0; s < SeriesCount; s++)
            {
                ref var value = ref _values[s * Capacity + newest];
                value = (float)((value * kept + values[s] * added) / (kept + added));
            }
            _times[newest] = time100Ns;
            return;
        }

        int slot;
        if (Count < Capacity) slot = Slot(Count++);
        else { slot = _head; _head = (_head + 1) % Capacity; }

        _times[slot] = time100Ns;
        for (var s = 0; s < SeriesCount; s++) _values[s * Capacity + slot] = values[s];
        _anchor = time100Ns;
    }

    /// <summary>Index 0 is the oldest point kept.</summary>
    public long TimeAt(int index) => _times[Slot(index)];

    public float ValueAt(int series, int index) => _values[series * Capacity + Slot(index)];

    /// <summary>The largest value from <see cref="FirstVisible"/> on; 0 when empty.</summary>
    public float MaxVisible(int series)
    {
        var max = 0f;
        for (var i = FirstVisible; i < Count; i++) max = Math.Max(max, ValueAt(series, i));
        return max;
    }

    int Slot(int index) => (_head + index) % Capacity;
}
