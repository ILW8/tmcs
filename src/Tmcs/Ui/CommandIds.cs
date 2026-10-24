using Tmcs.Sampling;

namespace Tmcs.Ui;

/// <summary>The command ids of the main menu items, as <c>WM_COMMAND</c> reports them.</summary>
internal static class CommandIds
{
    public const int Exit = 100;
    public const int AlwaysOnTop = 200;
    public const int RefreshNow = 300;

    // The four speeds must stay contiguous and in UpdateSpeed order: CheckMenuRadioItem takes them as one range.
    public const int SpeedHigh = 310;
    public const int SpeedNormal = 311;
    public const int SpeedLow = 312;
    public const int SpeedPaused = 313;

    /// <summary>The Update speed item for <paramref name="speed"/>.</summary>
    public static int ForSpeed(UpdateSpeed speed) => SpeedHigh + (int)speed;

    /// <summary>The speed that <paramref name="id"/> selects, if it is an Update speed item.</summary>
    public static bool TryGetSpeed(int id, out UpdateSpeed speed)
    {
        speed = (UpdateSpeed)(id - SpeedHigh);
        return id is >= SpeedHigh and <= SpeedPaused;
    }
}
