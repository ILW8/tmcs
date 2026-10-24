using System.Buffers.Binary;
using Tmcs.Native;

namespace Tmcs.Devices.Memory;

/// <summary>Reads memory slot data from the raw SMBIOS table. Works without elevation.</summary>
internal static unsafe class Smbios
{
    const int TableHeaderSize = 8;
    const int TypeMemoryArray = 16;
    const int TypeMemoryDevice = 17;
    const int TypeEndOfTable = 127;

    const int OffArrayDeviceCount = 0x0D;
    const int OffDeviceSize = 0x0C;
    const int OffDeviceFormFactor = 0x0E;
    const int OffDeviceSpeed = 0x15;
    const int OffDeviceConfiguredSpeed = 0x20;
    const int ConfiguredSpeedLength = 0x22;

    /// <summary>Speed of the fastest populated slot, populated and total slot counts, and the first populated slot's form factor. Zeros and blank on any failure.</summary>
    public static (int SpeedMhz, int SlotsUsed, int Slots, string FormFactor) ReadMemory()
    {
        try
        {
            var size = Kernel32.GetSystemFirmwareTable(Kernel32.RawSmbiosProvider, 0, null, 0);
            if (size <= TableHeaderSize) return (0, 0, 0, "");
            var buffer = new byte[size];
            fixed (byte* p = buffer)
                size = Kernel32.GetSystemFirmwareTable(Kernel32.RawSmbiosProvider, 0, p, size);
            if (size <= TableHeaderSize) return (0, 0, 0, "");
            return Parse(buffer.AsSpan(0, (int)size));
        }
        catch
        {
            return (0, 0, 0, "");
        }
    }

    /// <param name="raw">The <c>RawSMBIOSData</c> block: 8 header bytes, then the structures.</param>
    internal static (int SpeedMhz, int SlotsUsed, int Slots, string FormFactor) Parse(ReadOnlySpan<byte> raw)
    {
        int speed = 0, used = 0, slots = 0;
        var formFactor = "";

        var tableLength = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]), (uint)(raw.Length - TableHeaderSize));
        var table = raw.Slice(TableHeaderSize, tableLength);

        var offset = 0;
        while (offset + 4 <= table.Length)
        {
            int type = table[offset];
            int length = table[offset + 1];
            if (length < 4 || offset + length > table.Length) break;
            var structure = table.Slice(offset, length);

            if (type == TypeMemoryArray && length >= OffArrayDeviceCount + 2)
            {
                slots += BinaryPrimitives.ReadUInt16LittleEndian(structure[OffArrayDeviceCount..]);
            }
            else if (type == TypeMemoryDevice && length >= OffDeviceFormFactor + 1 &&
                     BinaryPrimitives.ReadUInt16LittleEndian(structure[OffDeviceSize..]) != 0)
            {
                used++;
                if (formFactor.Length == 0) formFactor = structure[OffDeviceFormFactor] switch { 0x09 => "DIMM", 0x0D => "SODIMM", _ => "" };

                var mhz = length >= ConfiguredSpeedLength
                    ? BinaryPrimitives.ReadUInt16LittleEndian(structure[OffDeviceConfiguredSpeed..])
                    : length >= OffDeviceSpeed + 2 ? BinaryPrimitives.ReadUInt16LittleEndian(structure[OffDeviceSpeed..]) : 0;
                speed = Math.Max(speed, mhz);
            }
            else if (type == TypeEndOfTable)
            {
                break;
            }

            // The strings after the formatted part end with a double zero.
            offset += length;
            while (offset + 1 < table.Length && (table[offset] != 0 || table[offset + 1] != 0)) offset++;
            offset += 2;
        }

        return (speed, used, slots, formFactor);
    }
}
