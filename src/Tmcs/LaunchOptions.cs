namespace Tmcs;

/// <summary>
/// How tmcs was started. Test mode is switched on by environment variables, never by command-line flags (the owner's choice);
/// tmcs ignores its arguments.
/// </summary>
/// <param name="DiagPath">The CSV file the diagnostics log writes to, or null outside test mode.</param>
/// <param name="Scenario">The scenario spec the test mode runs, or null.</param>
internal sealed record LaunchOptions(string? DiagPath, string? Scenario)
{
    /// <summary>The CSV output path.</summary>
    public const string DiagVariable = "TMCS_DIAG";

    /// <summary>The scenario spec.</summary>
    public const string ScenarioVariable = "TMCS_SCENARIO";

    public bool IsTestMode => DiagPath != null;

    /// <summary>Reads both variables through <paramref name="get"/>. A blank or whitespace value counts as unset.</summary>
    public static LaunchOptions FromEnvironment(Func<string, string?> get) => new(Read(get, DiagVariable), Read(get, ScenarioVariable));

    /// <summary>
    /// Reads both variables from this process's environment, then deletes them from it, so processes tmcs starts later do not inherit them.
    /// </summary>
    public static LaunchOptions FromProcessEnvironment()
    {
        var options = FromEnvironment(Environment.GetEnvironmentVariable);
        Environment.SetEnvironmentVariable(DiagVariable, null);
        Environment.SetEnvironmentVariable(ScenarioVariable, null);
        return options;
    }

    private static string? Read(Func<string, string?> get, string name) => get(name) is { } value && !string.IsNullOrWhiteSpace(value) ? value : null;
}
