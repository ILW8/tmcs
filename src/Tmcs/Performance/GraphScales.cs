namespace Tmcs.Performance;

/// <summary>
/// Auto-scale for the rate graphs: the smallest step at or above the visible maximum, from fixed lists. The 100 Kbps and
/// 100 KB/s floors reproduce every capture, since idle devices show them. Task Manager's own list also holds 10 Kbps and odd
/// steps such as 450 Kbps, which are left out (plan's call: the references couldn't verify its rule). Neither method allocates.
/// </summary>
internal static class GraphScales
{
    const float Kib = 1024f, Mib = Kib * 1024, Gib = Mib * 1024;

    /// <summary>Decimal bit rates.</summary>
    static readonly float[] NetworkSteps = [1e5f, 5e5f, 1e6f, 1e7f, 1e8f, 1e9f, 1e10f, 1e11f];

    static readonly string[] NetworkCaptions =
        ["100 Kbps", "500 Kbps", "1 Mbps", "10 Mbps", "100 Mbps", "1 Gbps", "10 Gbps", "100 Gbps"];

    /// <summary>Binary byte rates (1 KB = 1024).</summary>
    static readonly float[] DiskSteps =
        [100 * Kib, 500 * Kib, Mib, 10 * Mib, 100 * Mib, 250 * Mib, 500 * Mib, Gib, 10 * Gib, 100 * Gib];

    static readonly string[] DiskCaptions =
        ["100 KB/s", "500 KB/s", "1 MB/s", "10 MB/s", "100 MB/s", "250 MB/s", "500 MB/s", "1 GB/s", "10 GB/s", "100 GB/s"];

    public static (float Max, string Caption) Network(float bitsPerSecond) => Pick(NetworkSteps, NetworkCaptions, bitsPerSecond);

    public static (float Max, string Caption) Disk(float bytesPerSecond) => Pick(DiskSteps, DiskCaptions, bytesPerSecond);

    /// <summary>Above the top step the top step stays, and the painter clamps the line. NaN gets the floor, as the painter draws it as 0.</summary>
    static (float, string) Pick(float[] steps, string[] captions, float value)
    {
        var i = 0;
        while (i < steps.Length - 1 && value > steps[i]) i++;
        return (steps[i], captions[i]);
    }
}
