using Tmcs.Processes;

namespace Tmcs.Tests.Support;

/// <summary>Static-info source that counts <see cref="Query"/> calls per identity and returns values derived from the PID.</summary>
internal sealed class FakeInfo : IProcessInfoSource
{
    public Dictionary<ProcessIdentity, int> Calls { get; } = new();

    public int SidCacheCount => 0;

    public ProcessStaticInfo Query(ProcessIdentity id)
    {
        Calls[id] = Calls.GetValueOrDefault(id) + 1;
        return new ProcessStaticInfo("C:\\x\\" + id.Pid, "cmd " + id.Pid, "u", UacVirtualization.Disabled);
    }
}
