using Tmcs.Devices.Network;

namespace Tmcs.Tests.Devices;

/// <summary>Lists <see cref="Adapters"/> and reads <see cref="Counters"/> for any adapter; counts its calls.</summary>
internal sealed class FakeNetworkSource : INetworkSource
{
    public IReadOnlyList<AdapterInfo> Adapters { get; set; } = [];
    public AdapterCounters Counters { get; set; }
    public bool Succeeds { get; set; } = true;
    public WifiInfo? Wifi { get; set; }

    /// <summary>Set by the test as the system's notifications would; cleared by <see cref="Enumerate"/>, as the real source's is.</summary>
    public bool Changed { get; set; }

    public int Enumerations { get; private set; }
    public int Reads { get; private set; }

    public IReadOnlyList<AdapterInfo> Enumerate()
    {
        Enumerations++;
        Changed = false;
        return [.. Adapters];
    }

    public bool Read(ulong luid, out AdapterCounters counters)
    {
        Reads++;
        counters = Counters;
        return Succeeds;
    }

    public WifiInfo? ReadWifi(Guid interfaceGuid) => Wifi;

    public void Dispose() { }
}
