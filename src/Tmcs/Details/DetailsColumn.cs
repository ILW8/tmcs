namespace Tmcs.Details;

/// <summary>The Details columns, in their default left-to-right order. The first eight are visible by default.</summary>
internal enum DetailsColumn
{
    Name,
    Pid,
    Status,
    UserName,
    Cpu,
    ActivePrivateWorkingSet,
    CommandLine,
    UacVirtualization,
    Threads,
    Handles,
    CommitSize,
}
