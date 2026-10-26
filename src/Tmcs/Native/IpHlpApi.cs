using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class IpHlpApi
{
    public const uint AfUnspec = 0;
    public const uint ErrorBufferOverflow = 111;

    /// <param name="family"><c>AF_UNSPEC</c> and flags 0 (never <c>GAA_FLAG_SKIP_DNS_SERVER</c>, which blanks Tailscale's DNS suffix).</param>
    /// <param name="size">In: the buffer size. Out: the size needed when the result is <see cref="ErrorBufferOverflow"/>.</param>
    /// <returns>A Win32 error code; 0 on success.</returns>
    [LibraryImport("iphlpapi.dll")]
    public static partial uint GetAdaptersAddresses(uint family, uint flags, void* reserved, byte* addresses, uint* size);

    [LibraryImport("iphlpapi.dll")]
    public static partial uint ConvertInterfaceGuidToLuid(Guid* interfaceGuid, ulong* luid);

    /// <param name="row">A <c>MIB_IF_ROW2</c> (1352 bytes) with <c>InterfaceLuid</c> set.</param>
    [LibraryImport("iphlpapi.dll")]
    public static partial uint GetIfEntry2(byte* row);

    /// <param name="callback">Receives the context, the changed row and the notification type.</param>
    [LibraryImport("iphlpapi.dll")]
    public static partial uint NotifyIpInterfaceChange(ushort family, delegate* unmanaged<void*, void*, uint, void> callback, void* context, byte initialNotification, nint* handle);

    [LibraryImport("iphlpapi.dll")]
    public static partial uint NotifyUnicastIpAddressChange(ushort family, delegate* unmanaged<void*, void*, uint, void> callback, void* context, byte initialNotification, nint* handle);

    /// <summary>Waits for a callback that is running, so the context must outlive this call.</summary>
    [LibraryImport("iphlpapi.dll")]
    public static partial uint CancelMibChangeNotify2(nint handle);
}
