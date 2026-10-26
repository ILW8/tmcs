using System.Runtime.InteropServices;

namespace Tmcs.Native;

/// <summary>
/// The kernel-mode thunks (<c>D3DKMT*</c>) that gdi32 exports. They return an NTSTATUS and talk to dxgkrnl directly,
/// so nothing here loads a D3D runtime or a driver DLL into tmcs.
/// </summary>
internal static unsafe partial class Gdi32
{
    public const int KmtqaiAdapterType = 15;
    public const int KmtqaiGetSegmentSize = 3;
    public const int KmtqaiAdapterAddress = 6;
    public const int KmtqaiNodeMetadata = 25;
    public const int KmtqaiPhysicalAdapterPnpKey = 41;
    public const int KmtqaiQueryAdapterUniqueGuid = 60;
    public const int KmtqaiAdapterPerfData = 62;
    public const int KmtqaiDriverDescription = 65;

    /// <summary><c>KMTQAITYPE_ADAPTERTYPE</c> bits.</summary>
    public const uint AdapterTypeRenderSupported = 1 << 0;
    public const uint AdapterTypeSoftwareDevice = 1 << 2;

    /// <summary><c>D3DKMT_PNP_KEY_SOFTWARE</c>: the adapter's driver (class) key.</summary>
    public const uint PnpKeySoftware = 2;

    public const int QueryStatisticsAdapter = 0;
    public const int QueryStatisticsSegment = 3;

    /// <summary><c>sizeof(D3DKMT_QUERYSTATISTICS)</c>; the result union starts at 24, the query union at 0x320.</summary>
    public const int QueryStatisticsSize = 808;
    public const int QueryStatisticsResult = 24;
    public const int QueryStatisticsQuery = 0x320;

    /// <summary><c>sizeof(D3DKMT_NODEMETADATA)</c>, packed to 1: dxgkrnl rejects any other size.</summary>
    public const int NodeMetadataSize = 78;

    /// <summary><c>sizeof(D3DKMT_ADAPTER_PERFDATA)</c>.</summary>
    public const int AdapterPerfDataSize = 64;

    /// <summary><c>STATUS_BUFFER_TOO_SMALL</c>: an adapter arrived between the count and the listing.</summary>
    public const int StatusBufferTooSmall = unchecked((int)0xC0000023);

    /// <summary><c>D3DKMT_ADAPTERINFO</c>: 20 bytes, so an array of them has a 20-byte stride.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AdapterInfo
    {
        public uint Adapter;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint NumOfSources;
        public int PrecisePresentRegionsPreferred;
    }

    /// <summary><c>D3DKMT_ENUMADAPTERS3</c>: 24 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EnumAdapters3
    {
        /// <summary>Bit 0 includes compute-only adapters, bit 1 display-only ones.</summary>
        public ulong Filter;
        /// <summary>In: the length of <see cref="Adapters"/>. Out: the adapters listed.</summary>
        public uint NumAdapters;
        /// <summary>Null asks for the count only.</summary>
        public AdapterInfo* Adapters;
    }

    /// <summary><c>D3DKMT_QUERYADAPTERINFO</c>: 24 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct QueryAdapterInfo
    {
        public uint Adapter;
        public int Type;
        public void* PrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    /// <summary>Every adapter it lists comes back open; close each with <see cref="D3DKMTCloseAdapter"/>.</summary>
    [LibraryImport("gdi32.dll")]
    public static partial int D3DKMTEnumAdapters3(EnumAdapters3* enumAdapters);

    /// <param name="closeAdapter">A <c>D3DKMT_CLOSEADAPTER</c>: just the adapter handle.</param>
    [LibraryImport("gdi32.dll")]
    public static partial int D3DKMTCloseAdapter(uint* closeAdapter);

    [LibraryImport("gdi32.dll")]
    public static partial int D3DKMTQueryAdapterInfo(QueryAdapterInfo* queryAdapterInfo);

    /// <param name="queryStatistics">A zeroed <see cref="QueryStatisticsSize"/>-byte, 8-byte aligned <c>D3DKMT_QUERYSTATISTICS</c>, keyed by the adapter's LUID.</param>
    [LibraryImport("gdi32.dll")]
    public static partial int D3DKMTQueryStatistics(void* queryStatistics);
}
