using Microsoft.Win32.SafeHandles;
using Tmcs.Native;

namespace Tmcs.Devices.Disk;

/// <summary>
/// Disks come from the present <c>GUID_DEVINTERFACE_DISK</c> interfaces: probing <c>\\.\PhysicalDriveN</c> or PDH would also
/// find the containers VHDX. Every handle is opened with access 0, so nothing here needs elevation.
/// </summary>
internal sealed unsafe class SystemDiskSource : IDiskSource
{
    const uint IoctlStorageGetDeviceNumber = 0x2D1080;
    const uint IoctlStorageQueryProperty = 0x2D1400;
    const uint IoctlDiskGetDriveGeometryEx = 0x700A0;
    const uint IoctlDiskPerformance = 0x70020;
    const uint IoctlVolumeGetVolumeDiskExtents = 0x560000;

    const uint StorageDeviceProperty = 0;
    const uint StorageDeviceSeekPenaltyProperty = 7;

    const int PerformanceSize = 88;     // sizeof(DISK_PERFORMANCE)
    const int ErrorMoreData = 234;

    // Allocated once; Read must not allocate.
    readonly byte[] _performance = GC.AllocateArray<byte>(PerformanceSize, pinned: true);
    Dictionary<int, SafeFileHandle> _handles = [];

    public IReadOnlyList<DiskInfo> Enumerate()
    {
        var handles = new Dictionary<int, SafeFileHandle>();
        var disks = new List<DiskInfo>();
        var volumes = ReadVolumes(out var systemLetter);
        var pageFileLetters = ReadPageFileLetters();

        foreach (var path in InterfacePaths())
        {
            var handle = Kernel32.CreateFileW(path, 0, Kernel32.FileShareRead | Kernel32.FileShareWrite, 0, Kernel32.OpenExisting, 0, 0);
            if (handle.IsInvalid || !TryDeviceNumber(handle, out var number) || handles.ContainsKey(number))
            {
                handle.Dispose();
                continue;
            }
            handles[number] = handle;

            volumes.TryGetValue(number, out var volume);
            var letters = volume?.Letters.Order(StringComparer.Ordinal).ToArray() ?? [];
            disks.Add(new DiskInfo(
                Number: number,
                Letters: string.Join(' ', letters),
                Model: ReadModel(handle, out var removable),
                Capacity: ReadCapacity(handle),
                Formatted: volume?.Formatted ?? 0,
                IsSystem: systemLetter != null && letters.Contains(systemLetter),
                HasPageFile: letters.Any(pageFileLetters.Contains),
                Type: removable ? "Removable" : ReadIncursSeekPenalty(handle) ? "HDD" : "SSD"));
        }

        var old = _handles;
        _handles = handles;
        foreach (var h in old.Values) h.Dispose();

        disks.Sort((a, b) => a.Number.CompareTo(b.Number));
        return disks;
    }

    public bool Read(int number, out DiskCounters counters)
    {
        counters = default;
        if (!_handles.TryGetValue(number, out var handle)) return false;

        fixed (byte* p = _performance)
        {
            if (!Kernel32.DeviceIoControl(handle, IoctlDiskPerformance, null, 0, p, PerformanceSize, out _, 0)) return false;
            counters = new DiskCounters(
                BytesRead: *(long*)(p + 0),
                BytesWritten: *(long*)(p + 8),
                ReadTime: *(long*)(p + 16),
                WriteTime: *(long*)(p + 24),
                IdleTime: *(long*)(p + 32),
                ReadCount: *(uint*)(p + 40),
                WriteCount: *(uint*)(p + 44),
                QueryTime: *(long*)(p + 56));
            return true;
        }
    }

    public void Dispose()
    {
        foreach (var h in _handles.Values) h.Dispose();
        _handles = [];
    }

    sealed class VolumeInfo
    {
        public long Formatted;
        public readonly HashSet<string> Letters = [];
    }

    static List<string> InterfacePaths()
    {
        var paths = new List<string>();
        var guid = SetupApi.DiskInterfaceClass;
        var set = SetupApi.SetupDiGetClassDevsW(&guid, null, 0, SetupApi.DigcfPresent | SetupApi.DigcfDeviceInterface);
        if (set == -1) return paths;
        try
        {
            var buffer = new byte[1024];
            for (uint i = 0; ; i++)
            {
                var data = new SetupApi.DeviceInterfaceData { CbSize = (uint)sizeof(SetupApi.DeviceInterfaceData) };
                if (!SetupApi.SetupDiEnumDeviceInterfaces(set, null, &guid, i, &data)) break;

                fixed (byte* detail = buffer)
                {
                    *(uint*)detail = 8;     // sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W) on x64
                    if (SetupApi.SetupDiGetDeviceInterfaceDetailW(set, &data, detail, (uint)buffer.Length, null, null))
                        paths.Add(new string((char*)(detail + 4)));
                }
            }
        }
        finally { SetupApi.SetupDiDestroyDeviceInfoList(set); }
        return paths;
    }

    static bool TryDeviceNumber(SafeFileHandle handle, out int number)
    {
        // STORAGE_DEVICE_NUMBER: DeviceType u32, DeviceNumber u32, PartitionNumber u32.
        byte* buffer = stackalloc byte[12];
        number = 0;
        if (!Kernel32.DeviceIoControl(handle, IoctlStorageGetDeviceNumber, null, 0, buffer, 12, out _, 0)) return false;
        number = *(int*)(buffer + 4);
        return true;
    }

    static string ReadModel(SafeFileHandle handle, out bool removable)
    {
        removable = false;
        byte* buffer = stackalloc byte[1024];
        if (!Query(handle, StorageDeviceProperty, buffer, 1024)) return "";

        removable = buffer[10] != 0;
        var offset = *(uint*)(buffer + 16);
        if (offset == 0 || offset >= 1024) return "";

        // The ProductId is a zero-terminated ANSI string inside the descriptor.
        var length = 0;
        while (offset + length < 1024 && buffer[offset + length] != 0) length++;
        return new string((sbyte*)buffer, (int)offset, length, System.Text.Encoding.ASCII).Trim();
    }

    /// <summary>A failed query counts as a hard disk, the conservative guess.</summary>
    static bool ReadIncursSeekPenalty(SafeFileHandle handle)
    {
        byte* buffer = stackalloc byte[16];
        return !Query(handle, StorageDeviceSeekPenaltyProperty, buffer, 16) || buffer[8] != 0;
    }

    static bool Query(SafeFileHandle handle, uint propertyId, byte* output, uint outputSize)
    {
        // STORAGE_PROPERTY_QUERY: PropertyId u32, QueryType u32 (0 = standard), one byte of AdditionalParameters.
        uint* query = stackalloc uint[3];
        query[0] = propertyId;
        query[1] = 0;
        query[2] = 0;
        return Kernel32.DeviceIoControl(handle, IoctlStorageQueryProperty, query, 12, output, outputSize, out _, 0);
    }

    static long ReadCapacity(SafeFileHandle handle)
    {
        // DISK_GEOMETRY_EX: a 24-byte DISK_GEOMETRY, then DiskSize. IOCTL_DISK_GET_LENGTH_INFO would need read access.
        byte* buffer = stackalloc byte[256];
        return Kernel32.DeviceIoControl(handle, IoctlDiskGetDriveGeometryEx, null, 0, buffer, 256, out _, 0)
            ? *(long*)(buffer + 24)
            : 0;
    }

    /// <param name="systemLetter">The drive letter of the Windows directory, e.g. <c>C:</c>.</param>
    /// <returns>Each disk's letters and formatted size, from the extents of every volume.</returns>
    static Dictionary<int, VolumeInfo> ReadVolumes(out string? systemLetter)
    {
        var result = new Dictionary<int, VolumeInfo>();

        char* windows = stackalloc char[260];
        var length = Kernel32.GetSystemWindowsDirectoryW(windows, 260);
        systemLetter = length >= 2 && windows[1] == ':' ? new string(windows, 0, 2).ToUpperInvariant() : null;

        char* name = stackalloc char[64];
        var find = Kernel32.FindFirstVolumeW(name, 64);
        if (find == -1) return result;
        try
        {
            do
            {
                var volume = new string(name);      // \\?\Volume{guid}\
                ReadVolume(volume, result);
            } while (Kernel32.FindNextVolumeW(find, name, 64));
        }
        finally { Kernel32.FindVolumeClose(find); }
        return result;
    }

    static void ReadVolume(string volume, Dictionary<int, VolumeInfo> result)
    {
        using var handle = Kernel32.CreateFileW(volume.TrimEnd('\\'), 0, Kernel32.FileShareRead | Kernel32.FileShareWrite, 0, Kernel32.OpenExisting, 0, 0);
        if (handle.IsInvalid) return;

        // VOLUME_DISK_EXTENTS: count u32 @0, then 24-byte extents from @8 (DiskNumber u32 @0, ExtentLength i64 @16).
        var buffer = new byte[8 + 24 * 32];
        fixed (byte* p = buffer)
        {
            var ok = Kernel32.DeviceIoControl(handle, IoctlVolumeGetVolumeDiskExtents, null, 0, p, (uint)buffer.Length, out _, 0);
            if (!ok && System.Runtime.InteropServices.Marshal.GetLastWin32Error() != ErrorMoreData) return;

            var letters = VolumeLetters(volume);
            var count = Math.Min(*(uint*)p, 32u);       // more extents than fit are rare; the first 32 still list the disks
            for (var i = 0; i < count; i++)
            {
                var extent = p + 8 + 24 * i;
                var disk = *(int*)extent;
                if (!result.TryGetValue(disk, out var info)) result[disk] = info = new VolumeInfo();
                info.Formatted += *(long*)(extent + 16);
                foreach (var letter in letters) info.Letters.Add(letter);
            }
        }
    }

    /// <summary>Only drive-letter mounts (<c>C:\</c> becomes <c>C:</c>), not folder mounts.</summary>
    static List<string> VolumeLetters(string volume)
    {
        var letters = new List<string>();
        var size = 260u;
        var names = new char[size];
        fixed (char* p = names)
        {
            if (!Kernel32.GetVolumePathNamesForVolumeNameW(volume, p, size, out _)) return letters;
            for (var i = 0; i < names.Length && names[i] != 0;)
            {
                var end = Array.IndexOf(names, '\0', i);
                if (end - i == 3 && names[i + 1] == ':' && names[i + 2] == '\\')
                    letters.Add(char.ToUpperInvariant(names[i]) + ":");
                i = end + 1;
            }
        }
        return letters;
    }

    /// <returns>The drive letters (<c>C:</c>) of the page files, from names like <c>\??\C:\pagefile.sys</c>.</returns>
    static HashSet<string> ReadPageFileLetters()
    {
        var letters = new HashSet<string>();
        var buffer = new byte[4096];
        int status;
        int needed;
        while (true)
        {
            fixed (byte* p = buffer) status = NtDll.NtQuerySystemInformation(NtDll.SystemPageFileInformation, p, buffer.Length, out needed);
            if (status != NtDll.StatusInfoLengthMismatch) break;
            buffer = new byte[Math.Max(needed, buffer.Length * 2)];
        }
        if (status < 0) return letters;

        fixed (byte* p = buffer)
        {
            var entry = p;
            while (true)
            {
                // UNICODE_STRING @16: Length u16 @16, Buffer pointer @24.
                var chars = *(ushort*)(entry + 16) / 2;
                var name = (char*)*(nint*)(entry + 24);
                var colon = chars >= 2 ? new ReadOnlySpan<char>(name, chars).IndexOf(':') : -1;
                if (colon > 0) letters.Add(char.ToUpperInvariant(name[colon - 1]) + ":");

                var next = *(uint*)entry;
                if (next == 0) break;
                entry += next;
            }
        }
        return letters;
    }
}
