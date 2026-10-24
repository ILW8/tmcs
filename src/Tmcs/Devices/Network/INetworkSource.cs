namespace Tmcs.Devices.Network;

/// <param name="Luid">The interface LUID that <see cref="INetworkSource.Read"/> is keyed by.</param>
/// <param name="DnsSuffix">Null when empty; the pane omits the row then.</param>
internal sealed record AdapterInfo(Guid InterfaceGuid, ulong Luid, string Alias, string Description, string? DnsSuffix, bool IsWifi, string? IPv4, string? IPv6);

/// <summary><c>MIB_IF_ROW2</c> octet totals since the interface came up.</summary>
internal readonly record struct AdapterCounters(ulong InOctets, ulong OutOctets, bool IsUp);

/// <param name="PhyType"><c>802.11a</c>/<c>b</c>/<c>g</c>/<c>n</c>/<c>ac</c>/<c>ad</c>/<c>ax</c>, else empty.</param>
/// <param name="SignalQuality">0 to 100.</param>
internal sealed record WifiInfo(string Ssid, string PhyType, int SignalQuality);

internal interface INetworkSource : IDisposable
{
    /// <summary>Lists the Up, non-loopback, IP-bound adapters in the system's own order, and clears <see cref="Changed"/>.</summary>
    IReadOnlyList<AdapterInfo> Enumerate();

    /// <summary>False means the read failed (the adapter is gone) and nothing was written.</summary>
    bool Read(ulong luid, out AdapterCounters counters);

    /// <summary>Null when the adapter isn't connected or the query fails.</summary>
    WifiInfo? ReadWifi(Guid interfaceGuid);

    /// <summary>Set by the system's interface and address change notifications; cleared by <see cref="Enumerate"/>.</summary>
    bool Changed { get; }
}
