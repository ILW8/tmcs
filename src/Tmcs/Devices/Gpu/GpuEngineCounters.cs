using System.Globalization;
using System.Runtime.InteropServices;

namespace Tmcs.Devices.Gpu;

/// <summary>
/// Turns PDH's <c>\GPU Engine(*)\Running Time</c> raw values into each node's busy percentage. One instance is one
/// process's use of one node, so a node's percentage is the sum over its instances. Feed a sample's items to
/// <see cref="Accumulate"/>, then call <see cref="EndSample"/>; <see cref="NodePercent"/> reads the last completed sample.
/// </summary>
internal sealed class GpuEngineCounters
{
    readonly record struct Reading(long RunningTime, long TimeStamp);

    // The last sample's instances and this one's, swapped and cleared each sample: processes come and go all the time,
    // and only the live instances may stay.
    Dictionary<(uint Pid, long Luid, int Node), Reading> _previous = [];
    Dictionary<(uint Pid, long Luid, int Node), Reading> _current = [];

    // Busy fractions by node: this sample's running sums, and the last completed sample's.
    Dictionary<(long Luid, int Node), double> _sums = [];
    Dictionary<(long Luid, int Node), double> _completed = [];

    /// <summary>The instances the last completed sample held, to show the map follows the live ones.</summary>
    public int TrackedInstances => _previous.Count;

    /// <param name="instanceName">e.g. <c>pid_1234_luid_0x00000000_0x0001D8C7_phys_0_eng_0_engtype_3D</c>; anything else is skipped.</param>
    /// <param name="runningTime">The instance's running time, in 100 ns units.</param>
    /// <param name="timeStamp">When it was read, in 100 ns units.</param>
    public void Accumulate(ReadOnlySpan<char> instanceName, long runningTime, long timeStamp)
    {
        if (!TryParse(instanceName, out var key)) return;
        _current[key] = new Reading(runningTime, timeStamp);

        // An instance seen for the first time only sets its baseline.
        if (!_previous.TryGetValue(key, out var last)) return;
        var elapsed = timeStamp - last.TimeStamp;
        if (elapsed <= 0) return;

        // A pid reused within a tick, or a driver restart, sends the running time backwards: count it as idle.
        var busy = Math.Max(0, runningTime - last.RunningTime);
        CollectionsMarshal.GetValueRefOrAddDefault(_sums, (key.Luid, key.Node), out _) += (double)busy / elapsed;
    }

    public void EndSample()
    {
        (_previous, _current) = (_current, _previous);
        _current.Clear();
        (_completed, _sums) = (_sums, _completed);
        _sums.Clear();
    }

    /// <summary>0 to 100; 0 for a node no instance used in both of the last two samples.</summary>
    public float NodePercent(long luid, int node) =>
        _completed.TryGetValue((luid, node), out var busy) ? (float)Math.Clamp(busy * 100, 0, 100) : 0;

    /// <summary>Reads the pid, the LUID (high part, then low part, in hex) and the engine number, without allocating.</summary>
    static bool TryParse(ReadOnlySpan<char> name, out (uint Pid, long Luid, int Node) key)
    {
        key = default;
        if (!Skip(ref name, "pid_") || !Take(ref name, NumberStyles.None, out var pid)
            || !Skip(ref name, "_luid_0x") || !Take(ref name, NumberStyles.AllowHexSpecifier, out var high)
            || !Skip(ref name, "_0x") || !Take(ref name, NumberStyles.AllowHexSpecifier, out var low)
            || !Skip(ref name, "_phys_") || !Take(ref name, NumberStyles.None, out _)
            || !Skip(ref name, "_eng_") || !Take(ref name, NumberStyles.None, out var node)
            || node > int.MaxValue) return false;

        key = (pid, (long)high << 32 | low, (int)node);
        return true;
    }

    static bool Skip(ref ReadOnlySpan<char> name, ReadOnlySpan<char> literal)
    {
        if (!name.StartsWith(literal, StringComparison.Ordinal)) return false;
        name = name[literal.Length..];
        return true;
    }

    /// <summary>Takes the number that runs up to the next <c>_</c> or the end.</summary>
    static bool Take(ref ReadOnlySpan<char> name, NumberStyles style, out uint value)
    {
        var end = name.IndexOf('_');
        if (end < 0) end = name.Length;
        if (!uint.TryParse(name[..end], style, CultureInfo.InvariantCulture, out value)) return false;
        name = name[end..];
        return true;
    }
}
