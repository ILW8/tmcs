using Tmcs.Processes;

namespace Tmcs.Devices.Disk;

/// <summary>Series 0 is the active time in percent, 1 the read speed and 2 the write speed, both in bytes per second.</summary>
internal sealed class DiskDevice(IDiskSource source, DiskInfo info) : Device($"disk:{info.Number}", DeviceKind.Disk, 3)
{
    readonly float[] _values = new float[3];
    DiskCounters _previous;
    bool _hasPrevious;
    bool _failed;

    public DiskInfo Info { get; private set; } = info;

    public override string Title => Info.Letters.Length == 0 ? $"Disk {Info.Number}" : $"Disk {Info.Number} ({Info.Letters})";
    public override string Subtitle => Info.Model;
    public override bool Failed => _failed;

    public double ActivePercent { get; private set; }
    public double ResponseMs { get; private set; }
    public double ReadBytesPerSecond { get; private set; }
    public double WriteBytesPerSecond { get; private set; }

    /// <summary>A rescan's info for the same disk number: the letters may have changed. The history and the rate baseline are kept.</summary>
    public void Update(DiskInfo info) => Info = info;

    public override void Sample(long time100Ns, IReadOnlyList<RawProcess> snapshot)
    {
        if (!source.Read(Info.Number, out var c))
        {
            _failed = true;
            return;
        }
        _failed = false;

        var p = _previous;
        var elapsed = c.QueryTime - p.QueryTime;
        var backwards = c.BytesRead < p.BytesRead || c.BytesWritten < p.BytesWritten
            || c.ReadTime < p.ReadTime || c.WriteTime < p.WriteTime || c.IdleTime < p.IdleTime;

        if (!_hasPrevious || backwards || elapsed <= 0)
        {
            // The first read, a counter reset or a stalled clock: no rate can be trusted, so start over from here.
            ActivePercent = ResponseMs = ReadBytesPerSecond = WriteBytesPerSecond = 0;
        }
        else
        {
            ActivePercent = Math.Clamp(100.0 * (1.0 - (double)(c.IdleTime - p.IdleTime) / elapsed), 0, 100);

            // The counts are 32-bit and wrap; an unchecked uint subtraction turns a wrap into an ordinary delta.
            var ios = (long)unchecked(c.ReadCount - p.ReadCount) + unchecked(c.WriteCount - p.WriteCount);
            ResponseMs = ios == 0 ? 0 : (double)(c.ReadTime - p.ReadTime + c.WriteTime - p.WriteTime) / ios / 10_000;

            var seconds = elapsed / 1e7;
            ReadBytesPerSecond = (c.BytesRead - p.BytesRead) / seconds;
            WriteBytesPerSecond = (c.BytesWritten - p.BytesWritten) / seconds;
        }

        _previous = c;
        _hasPrevious = true;

        _values[0] = (float)ActivePercent;
        _values[1] = (float)ReadBytesPerSecond;
        _values[2] = (float)WriteBytesPerSecond;
        History.Add(time100Ns, _values);
    }
}
