namespace Tmcs.Diagnostics;

/// <summary>
/// One row of the diagnostics log: tmcs's own counters and its process's resource use at <see cref="Utc"/>.
/// </summary>
/// <param name="Utc">When it was taken. A <see cref="DateTimeKind.Local"/> time is converted; any other is taken as UTC.</param>
/// <param name="Step">The scenario step running: an action name, <c>done</c>, or <c>none</c> without a scenario.</param>
/// <param name="Samples">Samples the clock has taken since start.</param>
/// <param name="Rows">Rows in the process table.</param>
/// <param name="IconSlots">Images in the icon image list, the generic icon included.</param>
/// <param name="IconLive">Distinct image paths held by a row.</param>
/// <param name="IconPooled">Image paths nobody holds whose slot is still cached.</param>
/// <param name="SidCache">Account names cached for the User name column.</param>
/// <param name="PrivateBytes">The process's private bytes.</param>
/// <param name="ManagedBytes">Bytes allocated on the managed heap, as <see cref="GC.GetTotalMemory"/> reports without collecting.</param>
/// <param name="Handles">The process's kernel handles.</param>
/// <param name="Gdi">The process's GDI objects.</param>
/// <param name="User">The process's USER objects.</param>
/// <param name="CpuMs">The process's CPU time since start, in milliseconds.</param>
/// <param name="SwitchMs">How long switching into the running scenario step took, in milliseconds; 0 without a scenario.</param>
/// <param name="Devices">Devices the performance model lists, hidden adapters included.</param>
/// <param name="GpuInstances">GPU Engine counter instances the GPU source tracks: they must stay bounded under GPU process churn.</param>
internal readonly record struct DiagSnapshot(
    DateTime Utc,
    string Step,
    long Samples,
    int Rows,
    int IconSlots,
    int IconLive,
    int IconPooled,
    int SidCache,
    long PrivateBytes,
    long ManagedBytes,
    int Handles,
    int Gdi,
    int User,
    double CpuMs,
    double SwitchMs,
    int Devices,
    int GpuInstances);
