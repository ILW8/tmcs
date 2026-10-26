using Tmcs.Devices.Disk;
using Tmcs.Devices.Gpu;
using Tmcs.Devices.Network;
using Tmcs.Performance;
using Tmcs.Tests.Devices;

namespace Tmcs.Tests.Support;

/// <summary>
/// A machine of fakes: disks 0 (<c>C:</c>) and 4 (<c>D:</c>), the wired adapters <see cref="A"/> and <see cref="B"/>, and one
/// GPU with LUID 0x1D8C7. Every read succeeds until a test says otherwise. The extension methods reach the fakes inside.
/// </summary>
internal static class FakeDeviceSources
{
    public static readonly Guid A = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid B = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid C = new("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public static DeviceSources Create() => new(
        new FakeCpuSource(),
        new FakeMemorySource(),
        new FakeDiskSource { Disks = [Disk(0, "C:"), Disk(4, "D:")] },
        new FakeNetworkSource { Adapters = [Adapter(A, 1), Adapter(B, 2)], Counters = new(0, 0, IsUp: true) },
        new FakeGpuSource { Gpus = [Gpu(0x1D8C7)] });

    /// <summary>
    /// The owner's machine, as the sidebar tests need it: the 7950X's 32 logical processors, disks 0 to 4 (<c>C:</c> to <c>G:</c>),
    /// 12 wired adapters (<see cref="A"/>, <see cref="B"/> and <see cref="C"/> first, then <see cref="OwnerAdapterGuid"/> 4 to 12,
    /// LUID n for adapter n), and one GPU with LUID 0x1D8C7.
    /// </summary>
    public static DeviceSources CreateOwnerMachine() => new(
        new FakeCpuSource(FakeCpuSource.Ryzen()),
        new FakeMemorySource(),
        new FakeDiskSource { Disks = [.. Enumerable.Range(0, 5).Select(n => Disk(n, (char)('C' + n) + ":"))] },
        new FakeNetworkSource { Adapters = OwnerAdapters(), Counters = new(0, 0, IsUp: true) },
        new FakeGpuSource { Gpus = [Gpu(0x1D8C7)] });

    /// <summary>The 12 adapters of <see cref="CreateOwnerMachine"/>, in order.</summary>
    public static AdapterInfo[] OwnerAdapters() =>
        [Adapter(A, 1), Adapter(B, 2), Adapter(C, 3), .. Enumerable.Range(4, 9).Select(n => Adapter(OwnerAdapterGuid(n), (ulong)n))];

    /// <summary>Adapter n's interface GUID for n from 4: <c>0000000n-0000-…</c>.</summary>
    static Guid OwnerAdapterGuid(int n) => new(n, 0, 0, new byte[8]);

    public static DiskInfo Disk(int number, string letters) => new(number, letters, "Fake SSD", 1_000_000_000_000, 999_000_000_000, letters == "C:", false, "SSD");

    public static AdapterInfo Adapter(Guid guid, ulong luid, string ipv4 = "10.0.0.2") =>
        new(guid, luid, "Ethernet " + luid, "Fake adapter " + luid, null, false, ipv4, null);

    public static GpuInfo Gpu(long luid, int index = 0) =>
        new(index, luid, "Fake GPU", [GpuEngine.ThreeD, GpuEngine.Copy], 1L << 33, 1L << 34, 0, "1.0", "1/1/2026", "12 (FL 12.1)", "PCI bus 1, device 0, function 0");

    public static FakeCpuSource FakeCpu(this DeviceSources s) => (FakeCpuSource)s.Cpu;
    public static FakeMemorySource FakeMemory(this DeviceSources s) => (FakeMemorySource)s.Memory;
    public static FakeDiskSource FakeDisks(this DeviceSources s) => (FakeDiskSource)s.Disks;
    public static FakeNetworkSource FakeNetwork(this DeviceSources s) => (FakeNetworkSource)s.Network;
    public static FakeGpuSource FakeGpu(this DeviceSources s) => (FakeGpuSource)s.Gpu;
}
