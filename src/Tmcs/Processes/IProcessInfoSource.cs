namespace Tmcs.Processes;

internal interface IProcessInfoSource
{
    /// <summary>Looks up the static info for a newly seen process. Called exactly once per identity.</summary>
    ProcessStaticInfo Query(ProcessIdentity id);
}
