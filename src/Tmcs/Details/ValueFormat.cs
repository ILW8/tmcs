using Tmcs.Processes;

namespace Tmcs.Details;

/// <summary>
/// Cell text formats. Numbers use the current culture's group separator. Each format writes into a span and cuts its text to the
/// prefix that fits, so cells are drawn without allocating; the string forms wrap the span forms.
/// </summary>
internal static class ValueFormat
{
    /// <summary>Room for any number written here: a long with group separators of several characters each, and a suffix.</summary>
    private const int NumberCapacity = 128;

    /// <summary>Whole kilobytes, truncated, e.g. <c>12,345 K</c>.</summary>
    public static string Kilobytes(long bytes)
    {
        Span<char> text = stackalloc char[NumberCapacity];
        TryFormatKilobytes(bytes, text, out var written);
        return new string(text[..written]);
    }

    /// <summary>A percentage rounded to a whole number with at least two digits, e.g. <c>00</c>, <c>07</c>, <c>100</c>.</summary>
    public static string Cpu(double pct)
    {
        Span<char> text = stackalloc char[NumberCapacity];
        TryFormatCpu(pct, text, out var written);
        return new string(text[..written]);
    }

    public static string Count(int n)
    {
        Span<char> text = stackalloc char[NumberCapacity];
        TryFormatCount(n, text, out var written);
        return new string(text[..written]);
    }

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

    /// <summary>Writes <see cref="Kilobytes"/>.</summary>
    /// <returns>False when the text was cut to fit <paramref name="destination"/>.</returns>
    public static bool TryFormatKilobytes(long bytes, Span<char> destination, out int written) =>
        TryFormatNumber(bytes / 1024, "N0", " K", destination, out written);

    /// <summary>Writes <see cref="Cpu"/>.</summary>
    /// <returns>False when the text was cut to fit <paramref name="destination"/>.</returns>
    public static bool TryFormatCpu(double pct, Span<char> destination, out int written) =>
        TryFormatNumber((int)Math.Round(pct, MidpointRounding.AwayFromZero), "00", "", destination, out written);

    /// <summary>Writes <see cref="Count"/>.</summary>
    /// <returns>False when the text was cut to fit <paramref name="destination"/>.</returns>
    public static bool TryFormatCount(int n, Span<char> destination, out int written) =>
        TryFormatNumber(n, "N0", "", destination, out written);

    /// <summary>Writes a process id as <see cref="int.ToString()"/> does: plain digits.</summary>
    /// <returns>False when the text was cut to fit <paramref name="destination"/>.</returns>
    public static bool TryFormatPid(int pid, Span<char> destination, out int written) =>
        TryFormatNumber(pid, "", "", destination, out written);

    /// <summary>Writes <see cref="Status"/>.</summary>
    /// <returns>False when the text was cut to fit <paramref name="destination"/>.</returns>
    public static bool TryFormatStatus(ProcessStatus s, Span<char> destination, out int written) => TryCopy(Status(s), destination, out written);

    /// <summary>Writes <see cref="Uac"/>.</summary>
    /// <returns>False when the text was cut to fit <paramref name="destination"/>.</returns>
    public static bool TryFormatUac(UacVirtualization u, Span<char> destination, out int written) => TryCopy(Uac(u), destination, out written);

    /// <summary>Copies the prefix of <paramref name="text"/> that fits in <paramref name="destination"/>.</summary>
    /// <returns>False when the text was cut.</returns>
    public static bool TryCopy(ReadOnlySpan<char> text, Span<char> destination, out int written)
    {
        written = Math.Min(text.Length, destination.Length);
        text[..written].CopyTo(destination);
        return written == text.Length;
    }

    /// <summary>
    /// <paramref name="value"/> in <paramref name="format"/> with the current culture, then <paramref name="suffix"/>, cut to fit. The
    /// number is formatted into scratch space first, because <c>long.TryFormat</c> writes nothing into a destination too small.
    /// An int widened to long formats to the same text as the int.
    /// </summary>
    private static bool TryFormatNumber(long value, ReadOnlySpan<char> format, ReadOnlySpan<char> suffix, Span<char> destination, out int written)
    {
        Span<char> text = stackalloc char[NumberCapacity];
        if (!value.TryFormat(text, out var length, format) || !suffix.TryCopyTo(text[length..]))
        {
            written = 0; // no real culture's separators come near the capacity
            return false;
        }

        return TryCopy(text[..(length + suffix.Length)], destination, out written);
    }
}
