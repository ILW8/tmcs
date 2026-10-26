using Tmcs.Native;

namespace Tmcs.Devices.Memory;

/// <summary>
/// Two <c>NtQuerySystemInformation</c> calls per read. <c>GetPerformanceInfo</c> would give the commit and pool figures too,
/// but it takes its own process snapshot and costs about 12.6 ms.
/// </summary>
internal sealed unsafe class SystemMemorySource : IMemorySource
{
    // SYSTEM_PERFORMANCE_INFORMATION (uint page counts).
    const int OffAvailablePages = 44;
    const int OffCommittedPages = 48;
    const int OffCommitLimit = 52;
    const int OffPagedPoolPages = 112;
    const int OffNonPagedPoolPages = 116;

    // SYSTEM_MEMORY_LIST_INFORMATION (nuint page counts).
    const int MemoryListSize = 176;
    const int OffZero = 0;
    const int OffFree = 8;
    const int OffModified = 16;
    const int OffModifiedNoWrite = 24;
    const int OffPageCountByPriority = 40;
    const int PriorityCount = 8;

    // Pinned once, so a read allocates nothing.
    readonly byte[] _performance;
    readonly int _performanceLength;
    readonly byte[] _lists = GC.AllocateArray<byte>(MemoryListSize, pinned: true);
    readonly long _pageSize = Environment.SystemPageSize;

    public SystemMemorySource()
    {
        // The structure grows between Windows builds and the call insists on an exact size, so ask once for the real one.
        _performanceLength = 1024;
        _performance = GC.AllocateArray<byte>(_performanceLength, pinned: true);
        int status, needed;
        fixed (byte* p = _performance) status = NtDll.NtQuerySystemInformation(NtDll.SystemPerformanceInformation, p, _performanceLength, out needed);
        if (status == NtDll.StatusInfoLengthMismatch && needed > 0)
        {
            _performanceLength = needed;
            _performance = GC.AllocateArray<byte>(needed, pinned: true);
        }

        Info = ReadInfo();
    }

    public MemoryInfo Info { get; }

    public bool Read(out MemoryCounters counters)
    {
        counters = default;
        fixed (byte* perf = _performance)
        fixed (byte* lists = _lists)
        {
            if (NtDll.NtQuerySystemInformation(NtDll.SystemPerformanceInformation, perf, _performanceLength, out _) < 0) return false;
            if (NtDll.NtQuerySystemInformation(NtDll.SystemMemoryListInformation, lists, MemoryListSize, out _) < 0) return false;

            long standby = 0;
            for (var i = 0; i < PriorityCount; i++) standby += (long)*(nuint*)(lists + OffPageCountByPriority + 8 * i);

            counters = new MemoryCounters(
                Available: Pages(perf, OffAvailablePages),
                Committed: Pages(perf, OffCommittedPages),
                CommitLimit: Pages(perf, OffCommitLimit),
                PagedPool: Pages(perf, OffPagedPoolPages),
                NonPagedPool: Pages(perf, OffNonPagedPoolPages),
                Modified: ((long)*(nuint*)(lists + OffModified) + (long)*(nuint*)(lists + OffModifiedNoWrite)) * _pageSize,
                Standby: standby * _pageSize,
                Free: ((long)*(nuint*)(lists + OffFree) + (long)*(nuint*)(lists + OffZero)) * _pageSize);
            return true;
        }
    }

    long Pages(byte* block, int offset) => *(uint*)(block + offset) * _pageSize;

    static MemoryInfo ReadInfo()
    {
        var status = new Kernel32.MemoryStatusEx { Length = (uint)sizeof(Kernel32.MemoryStatusEx) };
        var total = Kernel32.GlobalMemoryStatusEx(ref status) ? (long)status.TotalPhys : 0;
        var installed = Kernel32.GetPhysicallyInstalledSystemMemory(out var kilobytes) ? (long)kilobytes * 1024 : total;
        var (speed, used, slots, formFactor) = Smbios.ReadMemory();
        return new MemoryInfo(installed, total, speed, used, slots, formFactor);
    }
}
