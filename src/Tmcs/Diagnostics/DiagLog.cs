using System.Globalization;
using System.Text;
using Tmcs.Sampling;

namespace Tmcs.Diagnostics;

/// <summary>
/// Test mode's CSV log. It creates the file's directory, replaces any file already there with the <see cref="Header"/>, then
/// writes one <see cref="DiagSnapshot"/> per tick of its own timer, every <see cref="IntervalMs"/>, apart from the sample clock.
/// Each line is flushed at once, so the file can be read while tmcs runs and keeps every row if it ends abruptly.
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

    /// <param name="csvPath">The file to write. Its directory is created if needed.</param>
    /// <param name="probe">Takes the snapshot for each row.</param>
    /// <param name="timer">Set to <see cref="IntervalMs"/> and started here; the caller keeps ownership.</param>
    public DiagLog(string csvPath, Func<DiagSnapshot> probe, ISampleTimer timer)
    {
        _probe = probe;
        _timer = timer;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csvPath))!);
        // Readers may open it meanwhile; they must allow writing, since this holds it open for writing.
        _writer = new StreamWriter(new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        WriteLine(Header);

        _timer.Tick += OnTick;
        _timer.IntervalMs = IntervalMs;
        _timer.Enabled = true;
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

    private void OnTick(object? sender, EventArgs e) => WriteLine(Format(_probe()));

    private void WriteLine(string line)
    {
        _writer.WriteLine(line);
        _writer.Flush();
    }
}
