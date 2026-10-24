using Tmcs.Processes;

namespace Tmcs.Devices.Network;

/// <summary>Series 0 is the receive speed (the filled, solid graph line) and 1 the send speed (dashed), both in bits per second.</summary>
internal sealed class NetworkDevice(INetworkSource source, AdapterInfo info) : Device("net:" + info.InterfaceGuid.ToString("B"), DeviceKind.Network, 2)
{
    readonly float[] _values = new float[2];
    AdapterCounters _previous;
    long _previousTime;
    bool _hasPrevious;
    bool _failed;

    public AdapterInfo Info { get; } = info;

    public override string Title => Info.IsWifi ? "Wi-Fi" : "Ethernet";
    public override string Subtitle => Info.Description;
    public override bool Failed => _failed;

    public double SendBitsPerSecond { get; private set; }
    public double ReceiveBitsPerSecond { get; private set; }
    public WifiInfo? Wifi { get; private set; }
    public string ConnectionType => Info.IsWifi ? Wifi?.PhyType ?? "" : "Ethernet";

    /// <summary>The reference couldn't find Task Manager's mapping; this one gives the captured 4 of 5 bars at quality 60-79.</summary>
    public static int SignalBars(int quality) => quality <= 0 ? 0 : Math.Min(5, quality / 20 + 1);

    public override void Sample(long time100Ns, IReadOnlyList<RawProcess> snapshot)
    {
        if (!source.Read(Info.Luid, out var c) || !c.IsUp)
        {
            _failed = true;
            return;
        }
        _failed = false;

        if (Info.IsWifi) Wifi = source.ReadWifi(Info.InterfaceGuid);

        var p = _previous;
        var elapsed = time100Ns - _previousTime;
        if (!_hasPrevious || c.InOctets < p.InOctets || c.OutOctets < p.OutOctets || elapsed <= 0)
        {
            // The first read, a counter reset or a stalled clock: no rate can be trusted, so start over from here.
            ReceiveBitsPerSecond = SendBitsPerSecond = 0;
        }
        else
        {
            var seconds = elapsed / 1e7;
            ReceiveBitsPerSecond = (c.InOctets - p.InOctets) * 8 / seconds;
            SendBitsPerSecond = (c.OutOctets - p.OutOctets) * 8 / seconds;
        }

        _previous = c;
        _previousTime = time100Ns;
        _hasPrevious = true;

        _values[0] = (float)ReceiveBitsPerSecond;
        _values[1] = (float)SendBitsPerSecond;
        History.Add(time100Ns, _values);
    }
}
