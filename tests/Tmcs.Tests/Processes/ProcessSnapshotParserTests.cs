using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Tmcs.Processes;

namespace Tmcs.Tests.Processes;

public class ProcessSnapshotParserTests
{
    [Fact]
    public unsafe void Parse_reads_entries_names_counters_and_suspension()
    {
        var buf = new byte[4096];
        fixed (byte* p = buf)
        {
            nint b = (nint)p;
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x00), 0x200);           // idle → next
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(0x30), 5_000_000);         // idle kernel time
            int e = 0x200, nameOff = e + 0x100 + 2 * 0x50;
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(e + 0x04), 2);
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x08), 40_960);
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x20), 133_000_000_000_000_000);
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x28), 20_000);
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x30), 30_000);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(e + 0x38), 14);           // "cmd.exe" bytes
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x40), b + nameOff);
            Encoding.Unicode.GetBytes("cmd.exe").CopyTo(buf, nameOff);
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(e + 0x48), 8);
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x50), 1234);
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0x58), 99);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(e + 0x60), 77);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(e + 0x64), 1);
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(e + 0xB8), 24_576);
            for (int t = 0; t < 2; t++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(e + 0x100 + t * 0x50 + 0x44), 5);
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(e + 0x100 + t * 0x50 + 0x48), 5);
            }
            var list = new List<RawProcess>();
            ProcessSnapshotParser.Parse(buf, b, list);

            Assert.Equal(2, list.Count);
            Assert.Equal(new RawProcess(0, 0, 0, "System Idle Process", 5_000_000, 0, 0, 0, 0, 0, 0, 0, false), list[0]);
            Assert.Equal(new RawProcess(1234, 99, 133_000_000_000_000_000, "cmd.exe", 30_000, 20_000, 40_960, 24_576, 2, 77, 1, 8, true), list[1]);
        }
    }

    [Fact]
    public void Parse_reads_the_working_set()
    {
        var buf = new byte[0x200 + 0x100];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x00), 0x200);
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(0x200 + 0x90), 0x9000);
        var list = new List<RawProcess>();
        ProcessSnapshotParser.Parse(buf, 0, list);

        Assert.Equal(0, list[0].WorkingSet);
        Assert.Equal(0x9000, list[1].WorkingSet);
    }

    [Fact]
    public void Live_snapshot_contains_System_and_this_process()
    {
        var list = new List<RawProcess>();
        new SystemProcessReader().Read(list);
        Assert.Contains(list, r => r.Pid == 4 && r.Name == "System");
        var me = list.Single(r => r.Pid == Environment.ProcessId);
        long expected = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToFileTimeUtc();
        Assert.InRange(me.CreateTime, expected - 10_000_000, expected + 10_000_000); // within 1 s
    }

    [Fact]
    public void Live_snapshot_grows_an_undersized_buffer_and_stays_correct_on_reuse()
    {
        var reader = new SystemProcessReader(initialBufferSize: 4096);
        var list = new List<RawProcess>();
        for (int i = 0; i < 3; i++)
        {
            reader.Read(list);
            Assert.Contains(list, r => r.Pid == 4 && r.Name == "System");
            Assert.Single(list, r => r.Pid == Environment.ProcessId);
        }
    }

    // A buffer of 0 bytes would double to 0 forever.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reader_refuses_a_buffer_size_that_is_not_positive(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SystemProcessReader(size));
}
