using System.Globalization;
using System.Runtime.InteropServices;
using Tmcs.Devices;
using Tmcs.Devices.Memory;
using Tmcs.Performance;
using Tmcs.Processes;

namespace Tmcs.Tests.Devices;

public sealed class MemoryDeviceTests : IDisposable
{
    const long Gib = FakeMemorySource.Gib;

    readonly CultureInfo _saved = CultureInfo.CurrentCulture;
    public MemoryDeviceTests() => CultureInfo.CurrentCulture = new("en-US");
    public void Dispose() => CultureInfo.CurrentCulture = _saved;

    static RawProcess Proc(int pid, int parent, string name, long workingSet) =>
        new(pid, parent, 0, name, 0, 0, 0, 0, 1, 0, 0, 8, false, workingSet);

    [Fact]
    public void In_use_is_total_minus_standby_free_and_modified()
    {
        var fake = new FakeMemorySource { Counters = new(0, 0, 0, 0, 0, Modified: 1 * Gib, Standby: 22 * Gib, Free: 3 * Gib) };
        var mem = new MemoryDevice(fake);
        mem.Sample(10_000_000, []);

        Assert.Equal(38 * Gib, mem.InUse);
        Assert.Equal(23 * Gib, mem.Cached);
        Assert.Equal(22 * Gib, mem.Standby);
        Assert.Equal(1, mem.History.Count);
        Assert.Equal(38 * Gib, mem.History.ValueAt(0, 0));
    }

    [Fact]
    public void Compressed_is_the_memory_compression_working_set()
    {
        var mem = new MemoryDevice(new FakeMemorySource());
        mem.Sample(10_000_000, [
            Proc(1234, 1, "Memory Compression", 1 * Gib),
            Proc(2000, 4, "Memory Compression", 3 * Gib),
            Proc(2001, 4, "other.exe", 9 * Gib)]);
        Assert.Equal(3 * Gib, mem.Compressed);

        mem.Sample(20_000_000, [Proc(1234, 1, "Memory Compression", 1 * Gib)]);
        Assert.Equal(0, mem.Compressed);
    }

    [Fact]
    public void Sidebar_text_uses_total_minus_available()
    {
        var total = (long)(63.7 * Gib);
        var fake = new FakeMemorySource
        {
            Counters = new(Available: (long)(25.3 * Gib), 0, 0, 0, 0, 0, 0, 0),
        };
        fake.Info = fake.Info with { TotalPhysical = total };
        var mem = new MemoryDevice(fake);
        mem.Sample(10_000_000, []);
        Assert.Equal("38.4/63.7 GB (60%)", mem.SidebarText);
    }

    [Fact]
    public void Hardware_reserved_is_installed_minus_total()
    {
        var mem = new MemoryDevice(new FakeMemorySource());
        Assert.Equal((long)(356.5 * (1 << 20)), mem.Info.HardwareReserved);
        Assert.Equal(PerfFormat.Size(mem.Info.Installed), mem.Subtitle);
        Assert.Equal("Memory", mem.Title);
        Assert.Equal("memory", mem.Key);
        Assert.Equal(DeviceKind.Memory, mem.Kind);
    }

    [Fact]
    public void Failed_read_appends_nothing()
    {
        var fake = new FakeMemorySource { Counters = new(0, 0, 0, 0, 0, 0, 22 * Gib, 3 * Gib) };
        var mem = new MemoryDevice(fake);
        mem.Sample(10_000_000, []);
        var inUse = mem.InUse;

        fake.Succeeds = false;
        fake.Counters = new(0, 0, 0, 0, 0, 0, 1 * Gib, 1 * Gib);
        mem.Sample(20_000_000, []);

        Assert.Equal(1, mem.History.Count);
        Assert.Equal(inUse, mem.InUse);
    }

    [Fact]
    public void Real_lists_add_up_to_total_physical()
    {
        var src = new SystemMemorySource();
        Assert.True(src.Read(out var c));
        Assert.True(c.Standby > 0 && c.Free > 0 && c.Modified > 0 && c.Available > 0);
        Assert.True(src.Info.TotalPhysical - (c.Standby + c.Free + c.Modified) > 0);

        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        Assert.True(GlobalMemoryStatusEx(ref status));
        Assert.Equal((long)status.TotalPhys, src.Info.TotalPhysical);
        Assert.InRange(c.Standby + c.Free, status.AvailPhys * 0.98, status.AvailPhys * 1.02);
    }

    [Fact]
    public void Real_counters_match_GetPerformanceInfo()
    {
        var src = new SystemMemorySource();
        Assert.True(src.Read(out var c));
        Assert.True(GetPerformanceInfo(out var pi, (uint)Marshal.SizeOf<PerformanceInformation>()));
        var page = (long)pi.PageSize;

        Within((long)pi.CommitTotal * page, c.Committed);
        Within((long)pi.CommitLimit * page, c.CommitLimit);
        Within((long)pi.KernelPaged * page, c.PagedPool);
        Within((long)pi.KernelNonpaged * page, c.NonPagedPool);

        static void Within(double expected, double actual) => Assert.InRange(actual, expected * 0.98, expected * 1.02);
    }

    [Fact]
    public void Real_smbios_reads_the_memory_slots()
    {
        var (speed, used, slots, _) = Smbios.ReadMemory();
        Assert.True(slots >= used && used >= 1, $"{used} of {slots}");
        Assert.True(speed > 0);
    }

    [Fact]
    public void Real_read_does_not_allocate()
    {
        var src = new SystemMemorySource();
        src.Read(out _);
        src.Read(out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) src.Read(out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache,
            KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }

    // Test-only imports, so the per-tick path in Native never carries GetPerformanceInfo.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    // GetPerformanceInfo lives in psapi.dll; kernel32 exports it as K32GetPerformanceInfo.
    [DllImport("kernel32.dll", EntryPoint = "K32GetPerformanceInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetPerformanceInfo(out PerformanceInformation info, uint size);
}
