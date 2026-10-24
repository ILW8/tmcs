namespace Tmcs.Processes;

/// <summary>
/// One live process in the table. Identity and static info are fixed at creation; the rest is refreshed every tick
/// by <see cref="ProcessTable"/>. <see cref="KernelTime"/> and <see cref="UserTime"/> double as the previous sample for the CPU delta.
/// </summary>
internal sealed class ProcessRow
{
    internal ProcessRow(ProcessIdentity id, string name, int parentPid, ProcessStaticInfo info)
    {
        Id = id;
        Name = name;
        ParentPid = parentPid;
        Info = info;
    }

    public ProcessIdentity Id { get; }
    public string Name { get; }
    public int ParentPid { get; }
    public ProcessStaticInfo Info { get; }

    /// <summary>Index into the icon list; assigned by the icon cache.</summary>
    public int IconSlot { get; set; }
    public ProcessStatus Status { get; set; }
    public double CpuPercent { get; set; }
    public long PrivateWorkingSet { get; set; }
    public long CommitSize { get; set; }
    public int Threads { get; set; }
    public int Handles { get; set; }
    public long KernelTime { get; set; }
    public long UserTime { get; set; }
}
