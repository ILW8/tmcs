using System.Globalization;

namespace Tmcs.Diagnostics;

/// <summary>A state test mode's scenario puts tmcs in. Each sets the whole state, whatever the previous step left.</summary>
internal enum ScenarioAction
{
    /// <summary>Restored, on the Details tab, sampling at Normal speed.</summary>
    Details,

    /// <summary>Minimized, sampling at Normal speed.</summary>
    Minimized,

    /// <summary>Restored, on the Details tab, paused.</summary>
    Paused,
}

/// <summary>One step of a scenario: <see cref="Action"/> is applied, then held for <see cref="Duration"/>.</summary>
internal readonly record struct ScenarioStep(ScenarioAction Action, TimeSpan Duration);

/// <summary>Reads a <c>TMCS_SCENARIO</c> spec.</summary>
internal static class ScenarioParser
{
    private const string Usage = "the format is action:minutes[,action:minutes]*, with action details, minimized or paused, e.g. details:10,minimized:0.5";

    /// <summary>The longest step a timer interval can hold: <see cref="int.MaxValue"/> ms, about 35791 minutes.</summary>
    private const double MaxMinutes = int.MaxValue / 60_000.0;

    /// <summary>
    /// Parses <c>action:minutes[,action:minutes]*</c>. Action names ignore case. Minutes are positive and may be fractional, with
    /// a decimal point whatever the current culture. Whitespace around a name or a number is ignored.
    /// </summary>
    /// <exception cref="FormatException">The spec is empty or any step is malformed. The message names the step.</exception>
    public static IReadOnlyList<ScenarioStep> Parse(string spec)
    {
        var steps = new List<ScenarioStep>();
        foreach (var part in spec.Split(','))
        {
            var fields = part.Split(':');
            if (fields.Length != 2) throw Bad(spec, part, "a step is action:minutes");

            var action = ParseAction(fields[0].Trim()) ?? throw Bad(spec, part, $"\"{fields[0].Trim()}\" is not an action");
            if (!double.TryParse(fields[1].Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var minutes)
                || !(minutes > 0) || minutes > MaxMinutes)
                throw Bad(spec, part, $"\"{fields[1].Trim()}\" is not a number of minutes above 0 and at most {MaxMinutes:F0}");

            steps.Add(new ScenarioStep(action, TimeSpan.FromMinutes(minutes)));
        }

        return steps;
    }

    /// <summary>The action's name in a spec and in the diagnostics log: lowercase.</summary>
    public static string Name(ScenarioAction action) => action switch
    {
        ScenarioAction.Details => "details",
        ScenarioAction.Minimized => "minimized",
        ScenarioAction.Paused => "paused",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    // Not Enum.TryParse, which also takes numbers and comma-separated lists.
    private static ScenarioAction? ParseAction(string name)
    {
        foreach (var action in Enum.GetValues<ScenarioAction>())
            if (string.Equals(name, Name(action), StringComparison.OrdinalIgnoreCase))
                return action;
        return null;
    }

    private static FormatException Bad(string spec, string part, string problem) =>
        new($"Bad scenario \"{spec}\": in step \"{part}\", {problem}; {Usage}.");
}
