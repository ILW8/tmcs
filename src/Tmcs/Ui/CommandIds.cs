using Tmcs.Actions;
using Tmcs.Sampling;

namespace Tmcs.Ui;

/// <summary>The command ids of the main menu items, as <c>WM_COMMAND</c> reports them, and of the sidebar's and the Details rows' menus.</summary>
internal static class CommandIds
{
    public const int Exit = 100;
    public const int RunNewTask = 101;
    public const int AlwaysOnTop = 200;
    public const int LinuxStyleCpu = 201;
    public const int RefreshNow = 300;

    // The four speeds must stay contiguous and in UpdateSpeed order: CheckMenuRadioItem takes them as one range.
    public const int SpeedHigh = 310;
    public const int SpeedNormal = 311;
    public const int SpeedLow = 312;
    public const int SpeedPaused = 313;

    /// <summary>The sidebar menu's check item that hides or shows the adapter it was opened on.</summary>
    public const int HideAdapter = 400;

    /// <summary>The sidebar menu's check item that shows the hidden adapters for the session.</summary>
    public const int ShowHiddenAdapters = 401;

    // The Details row menu (reference 1.2).
    public const int EndTask = 500;
    public const int EndProcessTree = 501;

    // The six priorities must stay contiguous, Realtime first and Low last, as the menu lists them: CheckMenuRadioItem takes them as one range.
    public const int PriorityRealtime = 510;
    public const int PriorityHigh = 511;
    public const int PriorityAboveNormal = 512;
    public const int PriorityNormal = 513;
    public const int PriorityBelowNormal = 514;
    public const int PriorityLow = 515;

    public const int SetAffinity = 520;
    public const int OpenFileLocation = 521;
    public const int Properties = 522;
    public const int CopyPid = 530;
    public const int CopyImagePath = 531;

    /// <summary>The Update speed item for <paramref name="speed"/>.</summary>
    public static int ForSpeed(UpdateSpeed speed) => SpeedHigh + (int)speed;

    /// <summary>The speed that <paramref name="id"/> selects, if it is an Update speed item.</summary>
    public static bool TryGetSpeed(int id, out UpdateSpeed speed)
    {
        speed = (UpdateSpeed)(id - SpeedHigh);
        return id is >= SpeedHigh and <= SpeedPaused;
    }

    /// <summary>The Set priority item for <paramref name="priority"/>. The menu runs from Realtime down, the enum from Low up.</summary>
    public static int ForPriority(ProcessPriority priority) => PriorityRealtime + (ProcessPriority.Realtime - priority);

    /// <summary>The priority that <paramref name="id"/> selects, if it is a Set priority item.</summary>
    public static bool TryGetPriority(int id, out ProcessPriority priority)
    {
        priority = ProcessPriority.Realtime - (id - PriorityRealtime);
        return id is >= PriorityRealtime and <= PriorityLow;
    }
}
