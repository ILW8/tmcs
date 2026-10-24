using System.Globalization;
using System.Text;
using Tmcs.Sampling;

namespace Tmcs.Diagnostics;

/// <summary>
/// Test mode's CSV log. It creates the file's directory, empties a diagnostics CSV already there (it refuses any other file, see
/// <see cref="Refusal"/>), writes the <see cref="Header"/>, then writes one <see cref="DiagSnapshot"/> per tick of its own timer,
/// every <see cref="IntervalMs"/>, apart from the sample clock. Each line is flushed at once, so the file can be read while tmcs
/// runs and keeps every row if it ends abruptly.
/// </summary>
internal sealed class DiagLog : IDisposable
{
    public const string Header =
        "utc,step,samples,rows,icon_slots,icon_live,icon_pooled,sid_cache,private_bytes,managed_bytes,handles,gdi,user,cpu_ms,switch_ms";

    /// <summary>Milliseconds between rows.</summary>
    public const int IntervalMs = 10_000;

    private readonly Func<DiagSnapshot> _probe;
    private readonly ISampleTimer _timer;
    private readonly StreamWriter _writer;
    private bool _disposed;

    /// <param name="csvPath">The file to write. Its directory is created if needed. A file already there must be a diagnostics CSV.</param>
    /// <param name="probe">Takes the snapshot for each row.</param>
    /// <param name="timer">Set to <see cref="IntervalMs"/> and started here; the caller keeps ownership.</param>
    /// <exception cref="IOException">The file could not be opened, or is there and is not a diagnostics CSV.</exception>
    /// <exception cref="UnauthorizedAccessException">The file or its directory may not be written.</exception>
    public DiagLog(string csvPath, Func<DiagSnapshot> probe, ISampleTimer timer)
    {
        _probe = probe;
        _timer = timer;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csvPath))!);
        _writer = new StreamWriter(OpenEmpty(csvPath), new UTF8Encoding(false));
        WriteLine(Header);

        _timer.Tick += OnTick;
        _timer.IntervalMs = IntervalMs;
        _timer.Enabled = true;
    }

    /// <summary>
    /// Why test mode must not write its log at <paramref name="csvPath"/>, or null when it may. An elevated tmcs inherits the
    /// user's environment, so <c>TMCS_DIAG</c> must not be able to make it destroy a file: the path must end in <c>.csv</c>, and a
    /// file already there must be a diagnostics CSV, whose first line is the <see cref="Header"/>. Writes nothing.
    /// </summary>
    /// <exception cref="IOException">A file already there could not be read, for example because another process has locked it.</exception>
    /// <exception cref="UnauthorizedAccessException">A file already there may not be read.</exception>
    public static string? Refusal(string csvPath)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(csvPath); // the file that would be opened
        }
        catch (ArgumentException)
        {
            return $"TMCS_DIAG is not a valid path: {csvPath}";
        }

        if (!fullPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return $"TMCS_DIAG must name a .csv file: {csvPath}";
        if (!File.Exists(fullPath)) return null;
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return IsDiagCsv(stream) ? null : $"TMCS_DIAG names a file that is not a tmcs diagnostics CSV, so tmcs will not replace it: {csvPath}";
    }

    /// <summary>One CSV line in <see cref="Header"/>'s order: invariant culture, the time as ISO 8601 UTC.</summary>
    public static string Format(DiagSnapshot s)
    {
        var utc = s.Utc.Kind == DateTimeKind.Local ? s.Utc.ToUniversalTime() : DateTime.SpecifyKind(s.Utc, DateTimeKind.Utc);
        return string.Create(CultureInfo.InvariantCulture,
            $"{utc:O},{s.Step},{s.Samples},{s.Rows},{s.IconSlots},{s.IconLive},{s.IconPooled},{s.SidCache},{s.PrivateBytes},{s.ManagedBytes},{s.Handles},{s.Gdi},{s.User},{s.CpuMs},{s.SwitchMs}");
    }

    /// <summary>Stops the timer and closes the file. The timer itself is the caller's to dispose.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Enabled = false;
        _timer.Tick -= OnTick;
        _writer.Dispose();
    }

    /// <summary>
    /// Opens the file empty, creating it if needed. Program has checked the path with <see cref="Refusal"/>; checking the content
    /// again on the open handle means a file swapped in meanwhile is not emptied either.
    /// </summary>
    private static FileStream OpenEmpty(string csvPath)
    {
        // Readers may open it meanwhile; they must allow writing, since this holds it open for writing.
        var stream = new FileStream(csvPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            if (stream.Length > 0 && !IsDiagCsv(stream))
                throw new IOException($"{csvPath} is not a tmcs diagnostics CSV, so tmcs will not replace it.");
            stream.SetLength(0); // also moves the position back to the start
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Whether the stream's first line is exactly the <see cref="Header"/>. Reads from the start, and only the header's length and one more character.</summary>
    private static bool IsDiagCsv(Stream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var start = new char[Header.Length + 1];
        var read = reader.ReadBlock(start, 0, start.Length);
        if (read < Header.Length || !start.AsSpan(0, Header.Length).SequenceEqual(Header)) return false;
        return read == Header.Length || start[Header.Length] is '\r' or '\n'; // the line ends there
    }

    private void OnTick(object? sender, EventArgs e) => WriteLine(Format(_probe()));

    private void WriteLine(string line)
    {
        _writer.WriteLine(line);
        _writer.Flush();
    }
}
