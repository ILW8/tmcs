using System.Runtime.InteropServices;

namespace Tmcs.Native;

/// <summary>
/// The native buffer one counter's array is read into, reused for every read. It grows when PDH asks for more and
/// never shrinks, so reads stop allocating once it has seen the largest array.
/// </summary>
internal sealed unsafe class PdhArrayBuffer : IDisposable
{
    byte* _data;
    uint _capacity;

    /// <summary>The items of the last read, followed by their names.</summary>
    public byte* Data => _data;

    /// <summary>Reads <c>PDH_FMT_COUNTERVALUE_ITEM_W</c> items (<see cref="Pdh.CounterValueItem"/> for a double format).</summary>
    public bool ReadFormatted(nint counter, uint format, out int count) => Read(counter, format, raw: false, out count);

    /// <summary>Reads <see cref="Pdh.RawCounterItem"/> items.</summary>
    public bool ReadRaw(nint counter, out int count) => Read(counter, 0, raw: true, out count);

    public void Dispose()
    {
        NativeMemory.Free(_data);
        _data = null;
        _capacity = 0;
    }

    bool Read(nint counter, uint format, bool raw, out int count)
    {
        count = 0;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var size = _capacity;
            uint items;
            var status = raw
                ? Pdh.PdhGetRawCounterArrayW(counter, ref size, out items, _data)
                : Pdh.PdhGetFormattedCounterArrayW(counter, format, ref size, out items, _data);
            if (status == Pdh.ErrorSuccess)
            {
                count = (int)items;
                return true;
            }
            if (status != Pdh.MoreData) return false;

            // The size PDH returns isn't reliable when the buffer was too small but not empty, so at least double.
            // The slack covers instances that appear before the next call (a GPU process adds one per engine).
            var wanted = Math.Max(size + Math.Max(4096, size / 8), _capacity * 2);
            _data = (byte*)NativeMemory.Realloc(_data, wanted);
            _capacity = wanted;
        }
        return false;
    }
}
