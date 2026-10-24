using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Tmcs.Native;

namespace Tmcs.Devices.Cpu;

/// <summary>
/// Reads <c>% Processor Utility</c> and <c>% Privileged Utility</c> for every logical processor, plus the total
/// <c>% Processor Performance</c>, from one PDH query that lives as long as the source.
/// </summary>
internal sealed unsafe class PdhCpuSource : ICpuSource
{
    const string UtilityPath = @"\Processor Information(*)\% Processor Utility";
    const string PrivilegedPath = @"\Processor Information(*)\% Privileged Utility";
    const string PerformancePath = @"\Processor Information(_Total)\% Processor Performance";
    const uint Format = Pdh.FmtDouble | Pdh.FmtNoCap100;

    /// <summary>
    /// The instance names of one counter's array, in the order PDH returned them, and the processor each one is.
    /// The names are fixed, so later reads only compare characters against it.
    /// </summary>
    sealed class InstanceMap
    {
        char[][] _names = [];
        /// <summary>Array position of the machine total (<c>_Total</c>), or -1.</summary>
        public int TotalPosition { get; private set; } = -1;
        int[] _processor = [];   // by array position: processor index, -1 for a total or anything unparsable

        public bool Matches(Pdh.CounterValueItem* items, int count)
        {
            if (count != _names.Length) return false;
            for (var i = 0; i < count; i++)
            {
                var name = _names[i];
                var p = items[i].Name;
                for (var c = 0; c < name.Length; c++)
                    if (p[c] != name[c]) return false;
                if (p[name.Length] != '\0') return false;
            }
            return true;
        }

        /// <summary>Allocates; runs at the first array read, and again only if the instance set changes.</summary>
        public void Build(Pdh.CounterValueItem* items, int count)
        {
            _names = new char[count][];
            var strings = new string[count];
            for (var i = 0; i < count; i++)
            {
                strings[i] = new string(items[i].Name);
                _names[i] = strings[i].ToCharArray();
            }
            _processor = new int[count];
            CpuInstanceNames.Rank(strings, _processor);
            TotalPosition = Array.IndexOf(strings, "_Total");
        }

        /// <summary>The processor index for the item at <paramref name="position"/>, -1 for a total or anything unparsable.</summary>
        public int ProcessorAt(int position) => _processor[position];
    }

    readonly nint _query;
    readonly nint _utility, _privileged, _performance;
    readonly InstanceMap _utilityMap = new(), _privilegedMap = new();
    readonly PdhArrayBuffer _utilityBuffer = new(), _privilegedBuffer = new();
    bool _collected;
    bool _disposed;

    public PdhCpuSource()
    {
        Info = ReadInfo();

        if (Pdh.PdhOpenQueryW(null, 0, out _query) != Pdh.ErrorSuccess)
            throw new InvalidOperationException("PdhOpenQueryW failed.");
        if (Pdh.PdhAddEnglishCounterW(_query, UtilityPath, 0, out _utility) != Pdh.ErrorSuccess
            || Pdh.PdhAddEnglishCounterW(_query, PrivilegedPath, 0, out _privileged) != Pdh.ErrorSuccess
            || Pdh.PdhAddEnglishCounterW(_query, PerformancePath, 0, out _performance) != Pdh.ErrorSuccess)
        {
            Pdh.PdhCloseQuery(_query);
            throw new InvalidOperationException("PdhAddEnglishCounterW failed.");
        }
    }

    public CpuInfo Info { get; }

    public bool Read(Span<CpuLoad> perProcessor, out double totalUtility, out double performancePercent)
    {
        totalUtility = performancePercent = 0;
        if (_disposed || Pdh.PdhCollectQueryData(_query) != Pdh.ErrorSuccess) return false;

        // A rate needs two collects, so the first read has nothing to report yet.
        if (!_collected)
        {
            _collected = true;
            perProcessor.Clear();
            return true;
        }

        if (!_utilityBuffer.ReadFormatted(_utility, Format, out var utilityCount)) return false;
        if (!_privilegedBuffer.ReadFormatted(_privileged, Format, out var privilegedCount)) return false;
        if (Pdh.PdhGetFormattedCounterValue(_performance, Format, out _, out var perf) != Pdh.ErrorSuccess
            || !IsValid(perf.CStatus)) return false;

        var utilityItems = (Pdh.CounterValueItem*)_utilityBuffer.Data;
        var privilegedItems = (Pdh.CounterValueItem*)_privilegedBuffer.Data;
        if (!_utilityMap.Matches(utilityItems, utilityCount)) _utilityMap.Build(utilityItems, utilityCount);
        if (!_privilegedMap.Matches(privilegedItems, privilegedCount)) _privilegedMap.Build(privilegedItems, privilegedCount);

        // Everything is read; only now write, so a failed read leaves the output alone.
        perProcessor.Clear();
        for (var i = 0; i < utilityCount; i++)
        {
            ref var item = ref utilityItems[i];
            if (!IsValid(item.CStatus)) continue;
            var processor = _utilityMap.ProcessorAt(i);
            if (i == _utilityMap.TotalPosition) totalUtility = Clamp(item.Value);
            else if (processor >= 0 && processor < perProcessor.Length)
                perProcessor[processor] = perProcessor[processor] with { Utility = (float)Clamp(item.Value) };
        }
        for (var i = 0; i < privilegedCount; i++)
        {
            ref var item = ref privilegedItems[i];
            if (!IsValid(item.CStatus)) continue;
            var processor = _privilegedMap.ProcessorAt(i);
            if (processor >= 0 && processor < perProcessor.Length)
                perProcessor[processor] = perProcessor[processor] with { Privileged = (float)Clamp(item.Value) };
        }
        // Above 100 when the processors boost, so only the lower end is clamped.
        performancePercent = double.IsNaN(perf.Value) ? 0 : Math.Max(0, perf.Value);
        return true;
    }

    public TimeSpan UpTime()
    {
        long* info = stackalloc long[6]; // SYSTEM_TIMEOFDAY_INFORMATION: BootTime at 0, CurrentTime at 8
        if (NtDll.NtQuerySystemInformation(NtDll.SystemTimeOfDayInformation, info, 48, out _) < 0) return TimeSpan.Zero;
        return TimeSpan.FromTicks(Math.Max(0, info[1] - info[0]));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Pdh.PdhCloseQuery(_query);
        _utilityBuffer.Dispose();
        _privilegedBuffer.Dispose();
    }

    static bool IsValid(uint status) => status is Pdh.CStatusValidData or Pdh.CStatusNewData;

    static double Clamp(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 100);

    static CpuInfo ReadInfo()
    {
        string name;
        double baseMhz;
        using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
        {
            // Kept as is: the name has a trailing space on some machines, and Task Manager shows it too.
            name = key?.GetValue("ProcessorNameString") as string ?? "";
            baseMhz = key?.GetValue("~MHz") is int mhz ? mhz : 0;
        }

        int sockets = 0, cores = 0, logical = 0;
        long l1 = 0, l2 = 0, l3 = 0;
        uint length = 0;
        Kernel32.GetLogicalProcessorInformationEx(Kernel32.RelationAll, null, ref length);
        if (length > 0)
        {
            var buffer = (byte*)NativeMemory.Alloc(length);
            try
            {
                if (Kernel32.GetLogicalProcessorInformationEx(Kernel32.RelationAll, buffer, ref length))
                {
                    // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: Relationship at 0, Size at 4, the union at 8.
                    for (uint offset = 0; offset + 8 <= length;)
                    {
                        var record = buffer + offset;
                        var relationship = *(int*)record;
                        var size = *(uint*)(record + 4);
                        if (size == 0) break;
                        switch (relationship)
                        {
                            case 0: // RelationProcessorCore: GroupCount at 30, GROUP_AFFINITY[] (16 bytes each, mask first) at 32
                                cores++;
                                var groups = *(ushort*)(record + 30);
                                for (var g = 0; g < groups; g++)
                                    logical += BitOperations.PopCount(*(ulong*)(record + 32 + 16 * g));
                                break;
                            case 2: // RelationCache: Level at 8, CacheSize at 12
                                var cache = (long)*(uint*)(record + 12);
                                switch (record[8])
                                {
                                    case 1: l1 += cache; break;
                                    case 2: l2 += cache; break;
                                    case 3: l3 += cache; break;
                                }
                                break;
                            case 3: // RelationProcessorPackage
                                sockets++;
                                break;
                        }
                        offset += size;
                    }
                }
            }
            finally { NativeMemory.Free(buffer); }
        }

        var virtualization = Kernel32.IsProcessorFeaturePresent(Kernel32.PfVirtFirmwareEnabled) ? "Enabled" : "Disabled";
        return new CpuInfo(name, baseMhz, sockets, cores, logical, virtualization, l1, l2, l3);
    }
}
