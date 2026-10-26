using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class WlanApi
{
    /// <summary><c>wlan_intf_opcode_current_connection</c>.</summary>
    public const int OpcodeCurrentConnection = 7;

    [LibraryImport("wlanapi.dll")]
    public static partial uint WlanOpenHandle(uint clientVersion, void* reserved, uint* negotiatedVersion, nint* handle);

    [LibraryImport("wlanapi.dll")]
    public static partial uint WlanCloseHandle(nint handle, void* reserved);

    /// <param name="data">Receives a <c>WLAN_CONNECTION_ATTRIBUTES</c> to release with <see cref="WlanFreeMemory"/>.</param>
    [LibraryImport("wlanapi.dll")]
    public static partial uint WlanQueryInterface(nint handle, Guid* interfaceGuid, int opcode, void* reserved, uint* dataSize, void** data, int* valueType);

    [LibraryImport("wlanapi.dll")]
    public static partial void WlanFreeMemory(void* memory);
}
