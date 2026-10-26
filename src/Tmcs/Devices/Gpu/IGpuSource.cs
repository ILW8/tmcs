namespace Tmcs.Devices.Gpu;

/// <summary>The engines the pane graphs, from <c>DXGK_ENGINE_TYPE</c>; every other type is <see cref="Other"/>.</summary>
internal enum GpuEngine { Other, ThreeD, Copy, VideoEncode, VideoDecode }

/// <param name="Index">The <c>N</c> of <c>GPU N</c>: the position among the listed GPUs.</param>
/// <param name="Luid">The adapter LUID, <c>HighPart &lt;&lt; 32 | LowPart</c>. A driver restart gives it a new one.</param>
/// <param name="NodeEngines">Node n's engine; PDH's <c>eng_n</c> is node n.</param>
/// <param name="DedicatedTotal">The physical VRAM, in bytes.</param>
/// <param name="SharedTotal">The system memory the GPU may use, in bytes.</param>
/// <param name="HardwareReserved">VRAM the driver keeps for itself: <see cref="DedicatedTotal"/> minus what the GPU's memory manager sees.</param>
/// <param name="DriverDate">In the current culture's short date format.</param>
/// <param name="DirectX">e.g. <c>12 (FL 12.1)</c>.</param>
/// <param name="Location">e.g. <c>PCI bus 1, device 0, function 0</c>.</param>
/// <remarks>A string that can't be read is blank.</remarks>
internal sealed record GpuInfo(
    int Index,
    long Luid,
    string Name,
    GpuEngine[] NodeEngines,
    long DedicatedTotal,
    long SharedTotal,
    long HardwareReserved,
    string DriverVersion,
    string DriverDate,
    string DirectX,
    string Location);

/// <summary>One GPU's values for one sample. Percentages are 0 to 100; the engine ones are each the busiest node of that engine type.</summary>
/// <param name="Utilization">The busiest node of any type.</param>
/// <param name="DedicatedUsed">Bytes resident in VRAM.</param>
/// <param name="SharedUsed">Bytes resident in shared system memory.</param>
/// <param name="TemperatureC">Null when the driver doesn't report one.</param>
internal readonly record struct GpuCounters(
    float Utilization,
    float ThreeD,
    float Copy,
    float VideoEncode,
    float VideoDecode,
    long DedicatedUsed,
    long SharedUsed,
    float? TemperatureC);

internal interface IGpuSource : IDisposable
{
    /// <summary>Lists the hardware render adapters and reads their static values. Called at startup and when the GPUs may have changed.</summary>
    IReadOnlyList<GpuInfo> Enumerate();

    /// <summary>
    /// Fills one entry per listed GPU, in <see cref="Enumerate"/>'s order, from one PDH collect. False means a read
    /// failed (a GPU is gone or changed) and the entries are not to be used.
    /// </summary>
    bool ReadAll(Span<GpuCounters> perGpu);

    /// <summary>
    /// The engine instances the utilization map holds, for the diagnostics log: it must follow the live instances. It
    /// counts every LUID PDH reports (the Basic Render Driver's included), not only the listed GPUs'.
    /// </summary>
    int TrackedInstances { get; }
}
