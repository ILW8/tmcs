namespace Tmcs.Performance;

/// <summary>
/// The samples the graphs' vertical grid lines sit on, shared by every graph: a graph whose step is s draws a line at every
/// s-th beat. Every timer sample is a beat. A refresh is one only when it comes at least one sampling interval after the last
/// beat, which happens only while paused, so quick refreshes never crowd the grid. At a steady speed every sample is a beat,
/// so the lines sit on every s-th point, as Task Manager's do. A ring that never grows, so noting allocates nothing.
/// </summary>
internal sealed class GridBeats
{
    /// <summary>More than High speed (0.5 s) puts in the 59-s window, plus the beat at its left edge.</summary>
    public const int Capacity = 128;

    readonly long[] _times = new long[Capacity];
    int _head;      // ring slot of the oldest beat
    long _noted;    // beats ever noted

    public int Count { get; private set; }

    /// <summary>
    /// Notes the sample at <paramref name="time100Ns"/> if it is a beat. <paramref name="timed"/> is whether the timer took it,
    /// and <paramref name="interval100Ns"/> is the sampling interval: the last running speed's while paused.
    /// </summary>
    public void Note(long time100Ns, bool timed, long interval100Ns)
    {
        if (Count > 0)
        {
            var last = TimeAt(Count - 1);
            if (time100Ns <= last || !timed && time100Ns - last < interval100Ns) return;
        }

        if (Count < Capacity) _times[(_head + Count++) % Capacity] = time100Ns;
        else { _times[_head] = time100Ns; _head = (_head + 1) % Capacity; }
        _noted++;
    }

    /// <summary>Index 0 is the oldest beat kept.</summary>
    public long TimeAt(int index) => _times[(_head + index) % Capacity];

    /// <summary>How many beats came before this one. A graph whose step is s has a line where this is a multiple of s.</summary>
    public long NumberAt(int index) => _noted - Count + index;
}
