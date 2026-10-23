namespace Tmcs.Processes;

/// <summary>Per-process values that never change after the process starts, so they are queried once when its row is created.</summary>
internal sealed record ProcessStaticInfo(string? ImagePath, string? CommandLine, string? UserName, UacVirtualization Uac)
{
    /// <summary>Used when nothing could be queried, e.g. a protected process or one that exited before the query.</summary>
    public static ProcessStaticInfo Blank { get; } = new(null, null, null, UacVirtualization.Unknown);
}
