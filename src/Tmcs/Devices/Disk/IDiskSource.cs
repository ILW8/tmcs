namespace Tmcs.Devices.Disk;

/// <param name="Letters">Drive letters of the disk's volumes, e.g. <c>C:</c> or <c>D: F:</c>; empty when it has none.</param>
/// <param name="Capacity">The disk's size in bytes.</param>
/// <param name="Formatted">The sum of the disk's volume extents, in bytes.</param>
/// <param name="Type"><c>SSD</c>, <c>HDD</c> or <c>Removable</c>.</param>
internal sealed record DiskInfo(int Number, string Letters, string Model, long Capacity, long Formatted, bool IsSystem, bool HasPageFile, string Type);

/// <summary><c>DISK_PERFORMANCE</c>: totals since boot, times in 100 ns. The counts are 32-bit and wrap.</summary>
internal readonly record struct DiskCounters(
    long BytesRead,
    long BytesWritten,
    long ReadTime,
    long WriteTime,
    long IdleTime,
    uint ReadCount,
    uint WriteCount,
    long QueryTime);

internal interface IDiskSource
{
    /// <summary>Lists the present disks by number, the ones <see cref="Read"/> reads. Called at startup and when the disks may have changed.</summary>
    IReadOnlyList<DiskInfo> Enumerate();

    /// <summary>False means the read failed (the disk is gone or changed) and nothing was written.</summary>
    bool Read(int number, out DiskCounters counters);
}
