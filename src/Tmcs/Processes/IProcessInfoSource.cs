namespace Tmcs.Processes;

internal interface IProcessInfoSource
{
    /// <summary>
    /// Looks up the static info for a newly seen process. Called exactly once per identity.
    /// Never throws: on any failure it returns <see cref="ProcessStaticInfo.Blank"/>, or partial info when some of the values could still be read.
    /// </summary>
    ProcessStaticInfo Query(ProcessIdentity id);
}
