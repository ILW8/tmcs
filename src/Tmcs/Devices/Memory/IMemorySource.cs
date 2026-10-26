namespace Tmcs.Devices.Memory;

/// <summary>Page lists and pools, all in bytes. <see cref="Free"/> includes the zeroed list; <see cref="Modified"/> includes the no-write list.</summary>
internal readonly record struct MemoryCounters(
    long Available,
    long Committed,
    long CommitLimit,
    long PagedPool,
    long NonPagedPool,
    long Modified,
    long Standby,
    long Free);

/// <summary>What does not change while tmcs runs.</summary>
/// <param name="Installed">The memory the machine has, from the firmware.</param>
/// <param name="TotalPhysical">The memory Windows can use.</param>
internal sealed record MemoryInfo(long Installed, long TotalPhysical, int SpeedMhz, int SlotsUsed, int Slots, string FormFactor)
{
    public long HardwareReserved => Installed - TotalPhysical;
}

internal interface IMemorySource
{
    MemoryInfo Info { get; }

    /// <summary>False means the read failed and nothing was written.</summary>
    bool Read(out MemoryCounters counters);
}
