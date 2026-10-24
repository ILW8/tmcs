namespace Tmcs.Performance;

/// <summary>
/// Task Manager's number formats for the Performance tab. Numbers use the current culture's decimal separator and no
/// group separator. Sizes are binary (KB = 2^10 ... PB = 2^50); bit rates are decimal.
/// </summary>
internal static class PerfFormat
{
    static readonly string[] SizeUnits = ["KB", "MB", "GB", "TB", "PB"];

    static double Round1(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    static string Fixed(double value, bool whole) =>
        whole ? Math.Round(value, MidpointRounding.AwayFromZero).ToString("0") : Round1(value).ToString("0.0");

    /// <summary>
    /// The shared precision rule: 0 is bare, one decimal below 100, none from 100. Rounding to a tenth first is how
    /// 931.496 GiB shows "932 GB".
    /// </summary>
    public static string Number(double value)
    {
        if (value == 0) return "0";
        return Fixed(Round1(value), Math.Abs(Round1(value)) >= 100);
    }

    public static string Size(double bytes)
    {
        var unit = 0;
        var scaled = bytes / 1024;
        while (unit < SizeUnits.Length - 1 && scaled / 1024 >= 1) { scaled /= 1024; unit++; }
        return Number(scaled) + " " + SizeUnits[unit];
    }

    public static string Gigabytes(double bytes) => Number(bytes / (1L << 30)) + " GB";

    /// <summary>"a/b unit", both with the precision the larger one gets.</summary>
    public static string Pair(double a, double b, string unit, double unitBytes)
    {
        a /= unitBytes; b /= unitBytes;
        var whole = Math.Abs(Round1(Math.Max(Math.Abs(a), Math.Abs(b)))) >= 100;
        return Fixed(a, whole) + "/" + Fixed(b, whole) + " " + unit;
    }

    public static string DiskRate(double bytesPerSecond) => bytesPerSecond switch
    {
        < 1048576 => Number(bytesPerSecond / 1024) + " KB/s",
        < 1073741824 => Number(bytesPerSecond / 1048576) + " MB/s",
        _ => Number(bytesPerSecond / 1073741824) + " GB/s",
    };

    /// <summary>Task Manager's rates are whole kilobytes a second times 8. K = 1000 is the owner's call.</summary>
    public static double QuantizeBits(double bitsPerSecond) =>
        Math.Round(bitsPerSecond / 8 / 1000, MidpointRounding.AwayFromZero) * 8000;

    static (double Divisor, string Unit) BitUnit(double quantizedBits) => quantizedBits switch
    {
        < 1e6 => (1e3, "Kbps"),
        < 1e9 => (1e6, "Mbps"),
        _ => (1e9, "Gbps"),
    };

    public static string BitRate(double bitsPerSecond)
    {
        var q = QuantizeBits(bitsPerSecond);
        var (divisor, unit) = BitUnit(q);
        return Number(q / divisor) + " " + unit;
    }

    /// <summary>The two values of the sidebar's "S: x R: y unit" line; one shared unit, carried by Receive only.</summary>
    public static (string Send, string Receive) SidebarRates(double sendBits, double receiveBits)
    {
        var s = QuantizeBits(sendBits);
        var r = QuantizeBits(receiveBits);
        var (divisor, unit) = BitUnit(Math.Max(s, r));
        return (Number(s / divisor), Number(r / divisor) + " " + unit);
    }

    public static string Percent(double percent) =>
        Math.Round(percent, MidpointRounding.AwayFromZero).ToString("0") + "%";

    public static string Ghz(double mhz) => (mhz / 1000).ToString("0.00") + " GHz";

    public static string UpTime(TimeSpan t) =>
        $"{(long)t.TotalDays}:{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}";

    public static string Milliseconds(double ms) => Number(ms) + " ms";

    public static string Celsius(double degrees) =>
        Math.Round(degrees, MidpointRounding.AwayFromZero).ToString("0") + " °C";
}
