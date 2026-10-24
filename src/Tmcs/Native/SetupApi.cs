using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class SetupApi
{
    /// <summary><c>DIGCF_PRESENT</c>: only devices that are present.</summary>
    public const uint DigcfPresent = 0x02;

    /// <summary><c>DIGCF_DEVICEINTERFACE</c>: list device interfaces of the class, not devices.</summary>
    public const uint DigcfDeviceInterface = 0x10;

    /// <summary><c>GUID_DEVINTERFACE_DISK</c>.</summary>
    public static readonly Guid DiskInterfaceClass = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

    /// <summary><c>SP_DEVICE_INTERFACE_DATA</c>: 32 bytes on x64. <see cref="CbSize"/> must be set before the call.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInterfaceData
    {
        public uint CbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public nuint Reserved;
    }

    /// <returns>A device information set to close with <see cref="SetupDiDestroyDeviceInfoList"/>, or -1 on failure.</returns>
    [LibraryImport("setupapi.dll", SetLastError = true)]
    public static partial nint SetupDiGetClassDevsW(Guid* classGuid, char* enumerator, nint hwndParent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, void* deviceInfoData, Guid* interfaceClassGuid, uint memberIndex, DeviceInterfaceData* interfaceData);

    /// <param name="detail">A <c>SP_DEVICE_INTERFACE_DETAIL_DATA_W</c>: <c>cbSize</c> (8 on x64), then the path from offset 4.</param>
    /// <param name="requiredSize">Receives the bytes needed.</param>
    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceInterfaceDetailW(nint deviceInfoSet, DeviceInterfaceData* interfaceData, byte* detail, uint detailSize, uint* requiredSize, void* deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);
}
