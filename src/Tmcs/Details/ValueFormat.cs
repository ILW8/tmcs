using Tmcs.Processes;

namespace Tmcs.Details;

/// <summary>Cell text formats. Numbers use the current culture's group separator.</summary>
internal static class ValueFormat
{
    /// <summary>Whole kilobytes, truncated, e.g. <c>12,345 K</c>.</summary>
    public static string Kilobytes(long bytes) => (bytes / 1024).ToString("N0") + " K";

    /// <summary>A percentage rounded to a whole number with at least two digits, e.g. <c>00</c>, <c>07</c>, <c>100</c>.</summary>
    public static string Cpu(double pct) => ((int)Math.Round(pct, MidpointRounding.AwayFromZero)).ToString("00");

    public static string Count(int n) => n.ToString("N0");

    public static string Status(ProcessStatus s) => s switch
    {
        ProcessStatus.Running => "Running",
        ProcessStatus.Suspended => "Suspended",
        ProcessStatus.NotResponding => "Not responding",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };

    public static string Uac(UacVirtualization u) => u switch
    {
        UacVirtualization.Unknown => "",
        UacVirtualization.NotAllowed => "Not allowed",
        UacVirtualization.Disabled => "Disabled",
        UacVirtualization.Enabled => "Enabled",
        _ => throw new ArgumentOutOfRangeException(nameof(u), u, null),
    };
}
