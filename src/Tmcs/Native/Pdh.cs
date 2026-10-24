using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class Pdh
{
    public const uint ErrorSuccess = 0;

    /// <summary><c>PDH_MORE_DATA</c>: the buffer was too small; the size argument now holds what is needed.</summary>
    public const uint MoreData = 0x800007D2;

    /// <summary><c>PDH_FMT_DOUBLE</c>: format values as <see cref="double"/>.</summary>
    public const uint FmtDouble = 0x00000200;

    /// <summary><c>PDH_FMT_NOCAP100</c>: don't cap percentages at 100 (utility counters go above it).</summary>
    public const uint FmtNoCap100 = 0x00008000;

    /// <summary><c>PDH_CSTATUS_VALID_DATA</c>.</summary>
    public const uint CStatusValidData = 0;

    /// <summary><c>PDH_CSTATUS_NEW_DATA</c>: valid, and the counter has just appeared.</summary>
    public const uint CStatusNewData = 1;

    /// <summary><c>PDH_FMT_COUNTERVALUE_ITEM_W</c> with a double value: 24 bytes on x64.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CounterValueItem
    {
        /// <summary>The instance name, in the same buffer after the items.</summary>
        public char* Name;
        public uint CStatus;
        public double Value;
    }

    /// <summary><c>PDH_FMT_COUNTERVALUE</c> with a double value.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CounterValue
    {
        public uint CStatus;
        public double Value;
    }

    /// <returns>A PDH status; <see cref="ErrorSuccess"/> on success.</returns>
    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    /// <summary>Adds a counter by its English path, whatever the display language.</summary>
    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhAddEnglishCounterW(nint query, string path, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCollectQueryData(nint query);

    /// <param name="bufferSize">In: bytes in <paramref name="buffer"/>. Out: bytes needed or used.</param>
    /// <param name="buffer">An array of <see cref="CounterValueItem"/> followed by the names; may be null with size 0 to ask for the size.</param>
    [LibraryImport("pdh.dll")]
    public static partial uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, out uint itemCount, void* buffer);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhGetFormattedCounterValue(nint counter, uint format, out uint counterType, out CounterValue value);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCloseQuery(nint query);
}
