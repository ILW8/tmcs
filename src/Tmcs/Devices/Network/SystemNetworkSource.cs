using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Tmcs.Native;

namespace Tmcs.Devices.Network;

/// <summary>
/// Adapters come from <c>GetAdaptersAddresses</c> in its own order, which is Task Manager's: <c>GetIfTable2</c> and
/// <c>NetworkInterface</c> add filter instances and vSwitches that aren't IP-bound.
/// </summary>
internal sealed unsafe class SystemNetworkSource : INetworkSource
{
    const int IfRowSize = 1352;         // sizeof(MIB_IF_ROW2)
    const int IfTypeLoopback = 24;
    const int IfTypeWifi = 71;
    const int OperStatusUp = 1;
    const ushort AfUnspecFamily = 0;
    const ushort AfInet = 2;
    const ushort AfInet6 = 23;

    // Allocated once; Read must not allocate.
    readonly byte[] _row = GC.AllocateArray<byte>(IfRowSize, pinned: true);
    byte[] _addresses = GC.AllocateArray<byte>(32 * 1024, pinned: true);
    GCHandle _self;
    nint _interfaceNotify;
    nint _addressNotify;
    nint _wlan;
    volatile bool _changed = true;
    bool _disposed;

    public SystemNetworkSource()
    {
        _self = GCHandle.Alloc(this);
        var context = (void*)GCHandle.ToIntPtr(_self);
        // Best effort: if a registration fails, Changed stays as it is and the model's other triggers still rescan.
        fixed (nint* interfaceNotify = &_interfaceNotify)
            IpHlpApi.NotifyIpInterfaceChange(AfUnspecFamily, &OnChange, context, 0, interfaceNotify);
        fixed (nint* addressNotify = &_addressNotify)
            IpHlpApi.NotifyUnicastIpAddressChange(AfUnspecFamily, &OnChange, context, 0, addressNotify);
    }

    public bool Changed => _changed;

    [UnmanagedCallersOnly]
    static void OnChange(void* context, void* row, uint notificationType)
    {
        try
        {
            if (GCHandle.FromIntPtr((nint)context).Target is SystemNetworkSource source) source._changed = true;
        }
        catch
        {
            // An exception must not unwind into native code.
        }
    }

    public IReadOnlyList<AdapterInfo> Enumerate()
    {
        _changed = false;
        var adapters = new List<AdapterInfo>();
        if (!FillAddresses()) return adapters;

        fixed (byte* buffer = _addresses)
        {
            for (var a = buffer; a != null; a = *(byte**)(a + 8))
            {
                var ifType = *(uint*)(a + 100);
                if (*(uint*)(a + 104) != OperStatusUp || ifType == IfTypeLoopback) continue;

                if (!Guid.TryParse(Marshal.PtrToStringAnsi(*(nint*)(a + 16)), out var guid)) continue;
                ulong luid;
                if (IpHlpApi.ConvertInterfaceGuidToLuid(&guid, &luid) != 0) continue;

                string? ipv4 = null, ipv6 = null;
                for (var u = *(byte**)(a + 24); u != null && (ipv4 == null || ipv6 == null); u = *(byte**)(u + 8))
                {
                    var sockaddr = *(byte**)(u + 16);
                    if (sockaddr == null) continue;
                    var family = *(ushort*)sockaddr;
                    if (family == AfInet && ipv4 == null)
                        ipv4 = new IPAddress(new ReadOnlySpan<byte>(sockaddr + 4, 4)).ToString();
                    else if (family == AfInet6 && ipv6 == null)
                        ipv6 = new IPAddress(new ReadOnlySpan<byte>(sockaddr + 8, 16), *(uint*)(sockaddr + 24)).ToString();
                }

                var dns = Marshal.PtrToStringUni(*(nint*)(a + 56));
                adapters.Add(new AdapterInfo(
                    guid, luid,
                    Marshal.PtrToStringUni(*(nint*)(a + 72)) ?? "",
                    Marshal.PtrToStringUni(*(nint*)(a + 64)) ?? "",
                    string.IsNullOrEmpty(dns) ? null : dns,
                    ifType == IfTypeWifi, ipv4, ipv6));
            }
        }
        return adapters;
    }

    /// <summary>Sizes the buffer by the first call and keeps it; it only grows when the list does.</summary>
    bool FillAddresses()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var size = (uint)_addresses.Length;
            uint result;
            fixed (byte* buffer = _addresses)
                result = IpHlpApi.GetAdaptersAddresses(IpHlpApi.AfUnspec, 0, null, buffer, &size);
            if (result == 0) return true;
            if (result != IpHlpApi.ErrorBufferOverflow) return false;
            _addresses = GC.AllocateArray<byte>((int)size + 4096, pinned: true);
        }
        return false;
    }

    public bool Read(ulong luid, out AdapterCounters counters)
    {
        fixed (byte* row = _row)
        {
            *(ulong*)row = luid;
            if (IpHlpApi.GetIfEntry2(row) != 0)
            {
                counters = default;
                return false;
            }
            counters = new AdapterCounters(*(ulong*)(row + 1208), *(ulong*)(row + 1280), *(uint*)(row + 1156) == OperStatusUp);
            return true;
        }
    }

    public WifiInfo? ReadWifi(Guid interfaceGuid)
    {
        if (_wlan == 0)
        {
            uint negotiated;
            nint handle;
            if (WlanApi.WlanOpenHandle(2, null, &negotiated, &handle) != 0) return null;
            _wlan = handle;
        }

        uint size;
        void* data;
        int valueType;
        if (WlanApi.WlanQueryInterface(_wlan, &interfaceGuid, WlanApi.OpcodeCurrentConnection, null, &size, &data, &valueType) != 0) return null;
        try
        {
            var attributes = (byte*)data;
            if (size < 580 || *(uint*)attributes != 1) return null;     // wlan_interface_state_connected

            var length = Math.Min(*(uint*)(attributes + 520), 32u);
            var ssid = Encoding.UTF8.GetString(attributes + 524, (int)length);
            var phy = *(int*)(attributes + 568) switch
            {
                4 => "802.11a",
                5 => "802.11b",
                6 => "802.11g",
                7 => "802.11n",
                8 => "802.11ac",
                9 => "802.11ad",
                10 => "802.11ax",
                _ => "",
            };
            return new WifiInfo(ssid, phy, (int)*(uint*)(attributes + 576));
        }
        finally
        {
            WlanApi.WlanFreeMemory(data);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Cancelling waits for a callback that is running, so the GCHandle can be freed only afterwards.
        if (_interfaceNotify != 0) IpHlpApi.CancelMibChangeNotify2(_interfaceNotify);
        if (_addressNotify != 0) IpHlpApi.CancelMibChangeNotify2(_addressNotify);
        if (_self.IsAllocated) _self.Free();
        if (_wlan != 0)
        {
            WlanApi.WlanCloseHandle(_wlan, null);
            _wlan = 0;
        }
    }
}
