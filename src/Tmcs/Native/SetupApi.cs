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

    /// <summary><c>GUID_DEVCLASS_DISPLAY</c>.</summary>
    public static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");

    /// <summary><c>DEVPROP_TYPE_UINT32</c>.</summary>
    public const uint DevPropTypeUInt32 = 0x07;

    /// <summary><c>DEVPROP_TYPE_FILETIME</c>.</summary>
    public const uint DevPropTypeFileTime = 0x10;

    /// <summary><c>DEVPROP_TYPE_STRING</c>.</summary>
    public const uint DevPropTypeString = 0x12;

    /// <summary><c>DEVPKEY_Device_LocationInfo</c>, a string.</summary>
    public static readonly DevPropKey DeviceLocationInfo = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 15);

    /// <summary><c>DEVPKEY_Device_BusNumber</c>, a UINT32.</summary>
    public static readonly DevPropKey DeviceBusNumber = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 23);

    /// <summary><c>DEVPKEY_Device_Address</c>, a UINT32: <c>(device &lt;&lt; 16) | function</c> on PCI.</summary>
    public static readonly DevPropKey DeviceAddress = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 30);

    /// <summary><c>DEVPKEY_Device_DriverDate</c>, a FILETIME.</summary>
    public static readonly DevPropKey DeviceDriverDate = new(new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"), 2);

    /// <summary><c>DEVPKEY_Device_DriverVersion</c>, a string.</summary>
    public static readonly DevPropKey DeviceDriverVersion = new(new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"), 3);

    /// <summary><c>DEVPROPKEY</c>: 20 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct DevPropKey(Guid fmtId, uint pid)
    {
        public readonly Guid FmtId = fmtId;
        public readonly uint Pid = pid;
    }

    /// <summary><c>SP_DEVINFO_DATA</c>: 32 bytes on x64. <see cref="CbSize"/> must be set before the call.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public nuint Reserved;
    }

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

    /// <returns>False past the last device (<c>ERROR_NO_MORE_ITEMS</c>).</returns>
    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiEnumDeviceInfo(nint deviceInfoSet, uint memberIndex, DeviceInfoData* deviceInfoData);

    /// <param name="propertyType">Receives the value's <c>DEVPROP_TYPE_*</c>.</param>
    /// <param name="requiredSize">Receives the bytes needed; may be null.</param>
    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDevicePropertyW(nint deviceInfoSet, DeviceInfoData* deviceInfoData, DevPropKey* propertyKey, uint* propertyType, byte* propertyBuffer, uint propertyBufferSize, uint* requiredSize, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);
}
