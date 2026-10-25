using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Tmcs.Native;

namespace Tmcs.Devices.Gpu;

/// <summary>
/// GPUs are the hardware render adapters that <c>D3DKMTEnumAdapters3</c> lists. Utilization comes from PDH's per-process
/// engine running times, as Task Manager's does: with hardware-accelerated GPU scheduling on, D3DKMT's global node
/// statistics read 0 for the 3D engine. Memory and temperature come from D3DKMT on every read; the static values from
/// D3DKMT, SetupAPI and the registry, once per <see cref="Enumerate"/>. Nothing here creates a D3D device: that maps
/// the driver's DLLs into tmcs for good. None of it needs elevation.
/// </summary>
internal sealed unsafe class SystemGpuSource : IGpuSource
{
    const string RunningTimePath = @"\GPU Engine(*)\Running Time";
    const string RegistryMachine = @"\REGISTRY\MACHINE\";
    const string DirectXKey = @"SOFTWARE\Microsoft\DirectX\";

    // DXGK_ENGINE_TYPE
    const int EngineThreeD = 1;
    const int EngineVideoDecode = 2;
    const int EngineVideoEncode = 3;
    const int EngineCopy = 6;

    /// <param name="Handle">Open until the next <see cref="Enumerate"/> or <see cref="Dispose"/>.</param>
    sealed record Adapter(uint Handle, long Luid, int Segments, GpuEngine[] NodeEngines);

    /// <param name="Address"><c>(device &lt;&lt; 16) | function</c>.</param>
    sealed record DisplayDevice(uint Bus, uint Address, string Location, string DriverVersion, string DriverDate);

    readonly GpuEngineCounters _engines = new();

    // Allocated once, so ReadAll doesn't allocate. Longs keep the D3DKMT buffers 8-byte aligned.
    readonly long[] _statistics = GC.AllocateArray<long>(Gdi32.QueryStatisticsSize / 8, pinned: true);
    readonly long[] _perfData = GC.AllocateArray<long>(Gdi32.AdapterPerfDataSize / 8, pinned: true);
    readonly PdhArrayBuffer _items = new();

    Adapter[] _adapters = [];
    readonly nint _query;
    readonly nint _counter;
    bool _disposed;

    public SystemGpuSource()
    {
        if (Pdh.PdhOpenQueryW(null, 0, out _query) != Pdh.ErrorSuccess)
        {
            _query = 0;
            Debug.WriteLine("SystemGpuSource: PdhOpenQueryW failed; no GPU is listed.");
            return;
        }
        if (Pdh.PdhAddEnglishCounterW(_query, RunningTimePath, 0, out _counter) != Pdh.ErrorSuccess)
        {
            Pdh.PdhCloseQuery(_query);
            _query = _counter = 0;
            Debug.WriteLine($"SystemGpuSource: no {RunningTimePath} counter; no GPU is listed.");
        }
    }

    public int TrackedInstances => _engines.TrackedInstances;

    /// <remarks>
    /// Lists nothing without the engine counter: Task Manager reads the same one, and a GPU without it would show a
    /// flat 0 % forever. Never filters by "has nodes" or "has memory": queries on an indirect-display adapter's LUID
    /// silently answer with the real GPU's data.
    /// </remarks>
    public IReadOnlyList<GpuInfo> Enumerate()
    {
        var gpus = new List<GpuInfo>();
        var kept = new List<Adapter>();
        if (!_disposed && _counter != 0)
        {
            Gdi32.AdapterInfo[] listed = [];
            var i = 0;
            try
            {
                listed = ListAdapters();
                List<DisplayDevice>? devices = null;
                for (; i < listed.Length; i++)
                {
                    var handle = listed[i].Adapter;
                    if (!IsHardwareRender(handle))
                    {
                        Close(handle);
                        continue;
                    }
                    devices ??= ReadDisplayDevices();
                    var luid = (long)listed[i].LuidHighPart << 32 | listed[i].LuidLowPart;
                    var info = ReadInfo(kept.Count, handle, luid, devices, out var segments);
                    gpus.Add(info);
                    kept.Add(new Adapter(handle, luid, segments, info.NodeEngines));    // last, so a throw leaves it to the catch
                }
            }
            catch (Exception e)
            {
                // A source never throws on the tick path, so this lists no GPU. Every listed handle is open: close the kept ones
                // and those not reached yet. The old ones close below, as the listing replaces them.
                Debug.WriteLine($"SystemGpuSource: listing the GPUs threw; none is listed. {e}");
                foreach (var a in kept) Close(a.Handle);
                for (; i < listed.Length; i++) Close(listed[i].Adapter);
                kept.Clear();
                gpus.Clear();
            }
        }

        var old = _adapters;
        _adapters = kept.ToArray();
        foreach (var a in old) Close(a.Handle);
        return gpus;
    }

    public bool ReadAll(Span<GpuCounters> perGpu)
    {
        var adapters = _adapters;
        if (_disposed || perGpu.Length < adapters.Length) return false;
        if (adapters.Length == 0) return true;
        if (!CollectEngines()) return false;

        for (var i = 0; i < adapters.Length; i++)
        {
            var a = adapters[i];
            if (!ReadMemory(a.Luid, a.Segments, out var dedicated, out var shared)) return false;

            // Each engine graph shows its busiest node (Copy has six), and Utilization the busiest of all.
            float utilization = 0, threeD = 0, copy = 0, encode = 0, decode = 0;
            for (var node = 0; node < a.NodeEngines.Length; node++)
            {
                var percent = _engines.NodePercent(a.Luid, node);
                utilization = Math.Max(utilization, percent);
                switch (a.NodeEngines[node])
                {
                    case GpuEngine.ThreeD: threeD = Math.Max(threeD, percent); break;
                    case GpuEngine.Copy: copy = Math.Max(copy, percent); break;
                    case GpuEngine.VideoEncode: encode = Math.Max(encode, percent); break;
                    case GpuEngine.VideoDecode: decode = Math.Max(decode, percent); break;
                }
            }
            perGpu[i] = new GpuCounters(utilization, threeD, copy, encode, decode, dedicated, shared, ReadTemperature(a.Handle));
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var a in _adapters) Close(a.Handle);
        _adapters = [];
        if (_query != 0) Pdh.PdhCloseQuery(_query);
        _items.Dispose();
    }

    /// <summary>One collect, fed to the engine counters item by item.</summary>
    bool CollectEngines()
    {
        if (Pdh.PdhCollectQueryData(_query) != Pdh.ErrorSuccess || !_items.ReadRaw(_counter, out var count)) return false;

        var items = (Pdh.RawCounterItem*)_items.Data;
        for (var i = 0; i < count; i++)
        {
            ref var item = ref items[i];
            // Processes that exit mid-collect leave items without valid data.
            if (item.CStatus is not (Pdh.CStatusValidData or Pdh.CStatusNewData)) continue;
            _engines.Accumulate(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(item.Name), item.FirstValue, item.TimeStamp);
        }
        _engines.EndSample();
        return true;
    }

    /// <summary>Sums the bytes resident in each segment: aperture 0 is VRAM, 1 is shared system memory.</summary>
    bool ReadMemory(long luid, int segments, out long dedicated, out long shared)
    {
        dedicated = shared = 0;
        fixed (long* buffer = _statistics)
        {
            var b = (byte*)buffer;
            for (var s = 0; s < segments; s++)
            {
                PrepareStatistics(b, Gdi32.QueryStatisticsSegment, luid);
                *(uint*)(b + Gdi32.QueryStatisticsQuery) = (uint)s;
                if (Gdi32.D3DKMTQueryStatistics(b) < 0) return false;

                // D3DKMT_QUERYSTATISTICS_SEGMENT_INFORMATION: BytesResident u64 @16, Aperture u32 @40.
                var resident = *(long*)(b + Gdi32.QueryStatisticsResult + 16);
                if (*(uint*)(b + Gdi32.QueryStatisticsResult + 40) == 0) dedicated += resident;
                else shared += resident;
            }
        }
        return true;
    }

    /// <summary>In °C; null when the driver doesn't report it (the Basic Render Driver fails the query).</summary>
    float? ReadTemperature(uint handle)
    {
        fixed (long* buffer = _perfData)
        {
            // D3DKMT_ADAPTER_PERFDATA: PhysicalAdapterIndex u32 @0 (in), Temperature u32 @56 in tenths of a degree.
            new Span<long>(buffer, _perfData.Length).Clear();
            if (!QueryAdapter(handle, Gdi32.KmtqaiAdapterPerfData, buffer, Gdi32.AdapterPerfDataSize)) return null;
            return *(uint*)((byte*)buffer + 56) / 10f;
        }
    }

    static void PrepareStatistics(byte* buffer, int type, long luid)
    {
        new Span<byte>(buffer, Gdi32.QueryStatisticsSize).Clear();
        *(int*)buffer = type;
        *(uint*)(buffer + 4) = (uint)luid;
        *(int*)(buffer + 8) = (int)(luid >> 32);
    }

    static bool QueryAdapter(uint handle, int type, void* data, int size)
    {
        var query = new Gdi32.QueryAdapterInfo { Adapter = handle, Type = type, PrivateDriverData = data, PrivateDriverDataSize = (uint)size };
        return Gdi32.D3DKMTQueryAdapterInfo(&query) >= 0;
    }

    static void Close(uint handle) => Gdi32.D3DKMTCloseAdapter(&handle);

    /// <summary>
    /// Filter 0 leaves out display-only adapters (DisplayLink, Parsec and other indirect displays) and compute-only ones.
    /// Every adapter returned is open.
    /// </summary>
    static Gdi32.AdapterInfo[] ListAdapters()
    {
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var query = new Gdi32.EnumAdapters3();
                if (Gdi32.D3DKMTEnumAdapters3(&query) < 0 || query.NumAdapters == 0) return [];

                var adapters = new Gdi32.AdapterInfo[query.NumAdapters];
                fixed (Gdi32.AdapterInfo* p = adapters)
                {
                    query.Adapters = p;
                    var status = Gdi32.D3DKMTEnumAdapters3(&query);
                    if (status == Gdi32.StatusBufferTooSmall) continue;     // an adapter arrived between the two calls
                    if (status < 0) return [];
                }
                return adapters[..(int)Math.Min(query.NumAdapters, (uint)adapters.Length)];
            }
        }
        catch (EntryPointNotFoundException)
        {
            Debug.WriteLine("SystemGpuSource: gdi32 has no D3DKMTEnumAdapters3 (Windows 10 2004 or later); no GPU is listed.");
        }
        return [];
    }

    /// <summary>Render-capable and not the Basic Render Driver or another software adapter.</summary>
    static bool IsHardwareRender(uint handle)
    {
        uint type;
        return QueryAdapter(handle, Gdi32.KmtqaiAdapterType, &type, sizeof(uint))
            && (type & Gdi32.AdapterTypeRenderSupported) != 0
            && (type & Gdi32.AdapterTypeSoftwareDevice) == 0;
    }

    static GpuInfo ReadInfo(int index, uint handle, long luid, List<DisplayDevice> devices, out int segments)
    {
        var chars = new char[4096];
        string name;
        fixed (char* p = chars)
            name = QueryAdapter(handle, Gdi32.KmtqaiDriverDescription, p, chars.Length * sizeof(char)) ? Terminated(chars) : "";

        var statistics = stackalloc long[Gdi32.QueryStatisticsSize / 8];
        var b = (byte*)statistics;
        PrepareStatistics(b, Gdi32.QueryStatisticsAdapter, luid);
        var (segmentCount, nodes) = Gdi32.D3DKMTQueryStatistics(b) >= 0
            ? AdapterCounts(new ReadOnlySpan<byte>(b + Gdi32.QueryStatisticsResult, 8))
            : (0, 0);
        segments = segmentCount;

        var engines = new GpuEngine[nodes];
        var metadata = stackalloc byte[Gdi32.NodeMetadataSize];
        for (var n = 0; n < nodes; n++)
        {
            // D3DKMT_NODEMETADATA: NodeOrdinalAndAdapterIndex u32 @0 (in; physical adapter 0 in the high word), EngineType i32 @4.
            new Span<byte>(metadata, Gdi32.NodeMetadataSize).Clear();
            *(uint*)metadata = (uint)n;
            if (!QueryAdapter(handle, Gdi32.KmtqaiNodeMetadata, metadata, Gdi32.NodeMetadataSize)) continue;
            engines[n] = *(int*)(metadata + 4) switch
            {
                EngineThreeD => GpuEngine.ThreeD,
                EngineVideoDecode => GpuEngine.VideoDecode,
                EngineVideoEncode => GpuEngine.VideoEncode,
                EngineCopy => GpuEngine.Copy,
                _ => GpuEngine.Other,
            };
        }

        // D3DKMT_SEGMENTSIZEINFO: DedicatedVideoMemorySize u64 @0, SharedSystemMemorySize u64 @16.
        var sizes = stackalloc long[3];
        long dedicatedVideo = 0, shared = 0;
        var haveSizes = QueryAdapter(handle, Gdi32.KmtqaiGetSegmentSize, sizes, 24);
        if (haveSizes)
        {
            dedicatedVideo = sizes[0];
            shared = sizes[2];
        }

        // The VRAM's physical size: DedicatedVideoMemorySize leaves out what the driver reserves (23.8 GB, not 24.0).
        var physical = ReadPhysicalMemorySize(handle, chars);
        var dedicatedTotal = physical > 0 ? physical : dedicatedVideo;

        // D3DKMT_ADAPTERADDRESS: BusNumber u32 @0, DeviceNumber u32 @4, FunctionNumber u32 @8.
        var address = stackalloc uint[3];
        DisplayDevice? device = null;
        if (QueryAdapter(handle, Gdi32.KmtqaiAdapterAddress, address, 12))
        {
            // Exact bus and address: DisplayLink devices report bus 0 too.
            var bus = address[0];
            var wanted = address[1] << 16 | address[2];
            device = devices.Find(d => d.Bus == bus && d.Address == wanted);
        }

        return new GpuInfo(
            Index: index,
            Luid: luid,
            Name: name,
            NodeEngines: engines,
            DedicatedTotal: dedicatedTotal,
            SharedTotal: shared,
            // Without the memory manager's size the difference would be the whole VRAM.
            HardwareReserved: haveSizes ? Math.Max(0, dedicatedTotal - dedicatedVideo) : 0,
            DriverVersion: device?.DriverVersion ?? "",
            DriverDate: device?.DriverDate ?? "",
            DirectX: ReadDirectX(handle, chars),
            Location: device?.Location ?? "");
    }

    /// <summary>
    /// <c>D3DKMT_QUERYSTATISTICS_ADAPTER_INFORMATION</c>: NbSegments u32 @0, NodeCount u32 @4. A count past
    /// <see cref="int.MaxValue"/> is corrupt and counts as none, rather than sizing an array with it.
    /// </summary>
    internal static (int Segments, int Nodes) AdapterCounts(ReadOnlySpan<byte> information) =>
        (Math.Max(0, MemoryMarshal.Read<int>(information)), Math.Max(0, MemoryMarshal.Read<int>(information[4..])));

    /// <summary><c>HardwareInformation.qwMemorySize</c> in the adapter's driver key; 0 when it can't be read.</summary>
    static long ReadPhysicalMemorySize(uint handle, char[] chars)
    {
        // D3DKMT_QUERY_PHYSICAL_ADAPTER_PNP_KEY: PhysicalAdapterIndex u32 @0, PnPKeyType u32 @4, pDest @8, pCchDest @16.
        var query = stackalloc byte[24];
        var count = (uint)chars.Length;
        string path;
        Array.Clear(chars);
        fixed (char* p = chars)
        {
            *(uint*)query = 0;
            *(uint*)(query + 4) = Gdi32.PnpKeySoftware;
            *(char**)(query + 8) = p;
            *(uint**)(query + 16) = &count;
            if (!QueryAdapter(handle, Gdi32.KmtqaiPhysicalAdapterPnpKey, query, 24)) return 0;
            path = Terminated(chars);
        }

        // e.g. \REGISTRY\MACHINE\SYSTEM\ControlSet001\Control\Class\{4d36e968-…}\0000
        if (!path.StartsWith(RegistryMachine, StringComparison.OrdinalIgnoreCase)) return 0;
        return ReadRegistry(path[RegistryMachine.Length..], "HardwareInformation.qwMemorySize") switch
        {
            long size => size,
            byte[] { Length: 8 } bytes => BitConverter.ToInt64(bytes),
            _ => 0,
        };
    }

    /// <summary>
    /// The DirectX adapter cache in the registry, keyed by the adapter's unique GUID: e.g. <c>12 (FL 12.1)</c> from
    /// <c>MaxD3D12FeatureLevel</c> 0xC100. A probe with a NULL D3D device would answer too, but maps the driver's DLLs in.
    /// </summary>
    static string ReadDirectX(uint handle, char[] chars)
    {
        string guid;
        fixed (char* p = chars)
        {
            // A WCHAR[40] "{GUID}".
            new Span<char>(p, 40).Clear();
            if (!QueryAdapter(handle, Gdi32.KmtqaiQueryAdapterUniqueGuid, p, 80)) return "";
            guid = Terminated(chars.AsSpan(0, 40));
        }
        if (guid.Length == 0) return "";

        if (ReadRegistry(DirectXKey + guid, "MaxD3D12FeatureLevel") is int level12 && level12 != 0) return "12 " + FeatureLevel(level12);
        if (ReadRegistry(DirectXKey + guid, "MaxD3D11FeatureLevel") is int level11 && level11 != 0) return "11 " + FeatureLevel(level11);
        return "";

        static string FeatureLevel(int level) => $"(FL {level >> 12}.{(level >> 8) & 0xF})";
    }

    static object? ReadRegistry(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue(name);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>The present display-class devices, for the driver and location values the adapter is joined to.</summary>
    static List<DisplayDevice> ReadDisplayDevices()
    {
        var devices = new List<DisplayDevice>();
        var guid = SetupApi.DisplayClass;
        var set = SetupApi.SetupDiGetClassDevsW(&guid, null, 0, SetupApi.DigcfPresent);
        if (set == -1) return devices;
        try
        {
            var buffer = new char[512];
            for (uint i = 0; ; i++)
            {
                var data = new SetupApi.DeviceInfoData { CbSize = (uint)sizeof(SetupApi.DeviceInfoData) };
                if (!SetupApi.SetupDiEnumDeviceInfo(set, i, &data)) break;

                uint bus, address;
                long fileTime;
                if (!TryProperty(set, &data, SetupApi.DeviceBusNumber, SetupApi.DevPropTypeUInt32, &bus, sizeof(uint))
                    || !TryProperty(set, &data, SetupApi.DeviceAddress, SetupApi.DevPropTypeUInt32, &address, sizeof(uint))) continue;

                // The FILETIME is midnight UTC of the driver's date, so it is read as UTC.
                var driverDate = TryProperty(set, &data, SetupApi.DeviceDriverDate, SetupApi.DevPropTypeFileTime, &fileTime, sizeof(long))
                    && fileTime >= 0 && fileTime <= DateTime.MaxValue.ToFileTimeUtc()
                        ? DateTime.FromFileTimeUtc(fileTime).ToString("d")
                        : "";
                devices.Add(new DisplayDevice(bus, address,
                    Location: ReadString(set, &data, SetupApi.DeviceLocationInfo, buffer),
                    DriverVersion: ReadString(set, &data, SetupApi.DeviceDriverVersion, buffer),
                    DriverDate: driverDate));
            }
        }
        finally { SetupApi.SetupDiDestroyDeviceInfoList(set); }
        return devices;
    }

    static bool TryProperty(nint set, SetupApi.DeviceInfoData* data, SetupApi.DevPropKey key, uint expectedType, void* value, uint size)
    {
        uint type;
        return SetupApi.SetupDiGetDevicePropertyW(set, data, &key, &type, (byte*)value, size, null, 0) && type == expectedType;
    }

    static string ReadString(nint set, SetupApi.DeviceInfoData* data, SetupApi.DevPropKey key, char[] buffer)
    {
        Array.Clear(buffer);
        fixed (char* p = buffer)
            return TryProperty(set, data, key, SetupApi.DevPropTypeString, p, (uint)((buffer.Length - 1) * sizeof(char))) ? Terminated(buffer) : "";
    }

    static string Terminated(ReadOnlySpan<char> chars)
    {
        var end = chars.IndexOf('\0');
        return new string(end < 0 ? chars : chars[..end]);
    }
}
