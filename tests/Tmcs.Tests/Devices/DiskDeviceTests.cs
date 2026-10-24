using Tmcs.Devices;
using Tmcs.Devices.Disk;

namespace Tmcs.Tests.Devices;

internal sealed class FakeDiskSource : IDiskSource
{
    public DiskCounters Counters { get; set; }
    public bool Succeeds { get; set; } = true;

    public IReadOnlyList<DiskInfo> Enumerate() => [];

    public bool Read(int number, out DiskCounters counters)
    {
        counters = Counters;
        return Succeeds;
    }

    public void Dispose() { }
}

public sealed class DiskDeviceTests
{
    const long Mib = 1 << 20;
    const long Second = 10_000_000;

    static DiskInfo Info(int number, string letters) => new(number, letters, "Test SSD", 1_000_000_000_000, 999_000_000_000, false, false, "SSD");

    static DiskCounters Counters(long query, long idle = 0, long read = 0, long written = 0, long readTime = 0, long writeTime = 0, uint reads = 0, uint writes = 0) =>
        new(read, written, readTime, writeTime, idle, reads, writes, query);

    static (DiskDevice Disk, FakeDiskSource Fake) Make()
    {
        var fake = new FakeDiskSource();
        return (new DiskDevice(fake, Info(0, "C:")), fake);
    }

    static void Read(DiskDevice disk, FakeDiskSource fake, long time, DiskCounters counters)
    {
        fake.Counters = counters;
        disk.Sample(time, []);
    }

    [Fact]
    public void Rates_come_from_counter_deltas()
    {
        var (disk, fake) = Make();
        Read(disk, fake, Second, Counters(1000 * Second));
        Read(disk, fake, 2 * Second, Counters(1001 * Second, idle: 2_500_000, read: Mib, written: 2 * Mib,
            readTime: 20_000, writeTime: 12_000, reads: 4, writes: 4));

        Assert.Equal(75, disk.ActivePercent, 6);
        Assert.Equal(Mib, disk.ReadBytesPerSecond, 3);
        Assert.Equal(2 * Mib, disk.WriteBytesPerSecond, 3);
        Assert.Equal(0.4, disk.ResponseMs, 9);
        Assert.Equal(2, disk.History.Count);
        Assert.Equal(75f, disk.History.ValueAt(0, 1), 3);
    }

    [Fact]
    public void First_read_is_zero_not_a_spike()
    {
        var (disk, fake) = Make();
        Read(disk, fake, Second, Counters(5000 * Second, idle: 100 * Second, read: 900 * Mib, written: 800 * Mib,
            readTime: 5 * Second, writeTime: 6 * Second, reads: 1_000_000, writes: 2_000_000));

        Assert.Equal(0, disk.ActivePercent);
        Assert.Equal(0, disk.ResponseMs);
        Assert.Equal(0, disk.ReadBytesPerSecond);
        Assert.Equal(0, disk.WriteBytesPerSecond);
        Assert.Equal(1, disk.History.Count);
        for (var s = 0; s < 3; s++) Assert.Equal(0f, disk.History.ValueAt(s, 0));
    }

    [Fact]
    public void Counters_going_backwards_reset_the_baseline()
    {
        var (disk, fake) = Make();
        Read(disk, fake, Second, Counters(1000 * Second, read: 10 * Mib));
        Read(disk, fake, 2 * Second, Counters(1001 * Second, read: 1 * Mib));

        Assert.Equal(0, disk.ReadBytesPerSecond);
        Assert.Equal(0, disk.ActivePercent);

        Read(disk, fake, 3 * Second, Counters(1002 * Second, read: 3 * Mib));
        Assert.Equal(2 * Mib, disk.ReadBytesPerSecond, 3);
    }

    [Fact]
    public void Io_counts_wrapping_past_uint_max_keep_working()
    {
        var (disk, fake) = Make();
        Read(disk, fake, Second, Counters(1000 * Second, reads: 0xFFFFFFFE, writes: 0xFFFFFFFF));
        Read(disk, fake, 2 * Second, Counters(1001 * Second, idle: 2_500_000, read: Mib, written: 2 * Mib,
            readTime: 20_000, writeTime: 12_000, reads: 2, writes: 3));

        Assert.Equal(0.4, disk.ResponseMs, 9);
        Assert.Equal(Mib, disk.ReadBytesPerSecond, 3);
        Assert.Equal(2 * Mib, disk.WriteBytesPerSecond, 3);
    }

    [Fact]
    public void Title_with_and_without_letters()
    {
        var with = new DiskDevice(new FakeDiskSource(), Info(4, "C:"));
        var without = new DiskDevice(new FakeDiskSource(), Info(5, ""));

        Assert.Equal("Disk 4 (C:)", with.Title);
        Assert.Equal("Disk 5", without.Title);
        Assert.Equal("disk:4", with.Key);
        Assert.Equal("Test SSD", with.Subtitle);
        Assert.Equal(DeviceKind.Disk, with.Kind);
    }

    [Fact]
    public void Failed_read_marks_the_device_failed()
    {
        var (disk, fake) = Make();
        Read(disk, fake, Second, Counters(1000 * Second));
        Assert.False(disk.Failed);

        fake.Succeeds = false;
        disk.Sample(2 * Second, []);

        Assert.True(disk.Failed);
        Assert.Equal(1, disk.History.Count);
    }

    [Fact]
    public void Real_listing_has_one_system_disk()
    {
        using var source = new SystemDiskSource();
        var disks = source.Enumerate();

        Assert.NotEmpty(disks);
        Assert.Equal(disks.Select(d => d.Number).Distinct().Order(), disks.Select(d => d.Number));
        Assert.Single(disks, d => d.IsSystem);
        Assert.All(disks, d =>
        {
            Assert.True(d.Capacity > 0);
            Assert.True(d.Formatted <= d.Capacity);
            Assert.NotEmpty(d.Model);
        });
    }

    [Fact]
    public void Real_reads_work_without_elevation()
    {
        using var source = new SystemDiskSource();
        var disks = source.Enumerate();
        Assert.NotEmpty(disks);

        foreach (var d in disks)
        {
            Assert.True(source.Read(d.Number, out var first));
            Thread.Sleep(20);
            Assert.True(source.Read(d.Number, out var second));
            Assert.True(second.QueryTime > first.QueryTime);
        }
    }
}
