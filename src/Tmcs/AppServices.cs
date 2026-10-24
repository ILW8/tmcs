using Tmcs.Icons;
using Tmcs.Native;
using Tmcs.Processes;
using Tmcs.Sampling;
using Tmcs.Settings;

namespace Tmcs;

/// <summary>
/// The outside world <see cref="MainForm"/> runs against. Tests pass fakes.
/// The form takes ownership of <see cref="GenericIcon"/> (it destroys it through <see cref="Icons"/>) and of <see cref="Timer"/>
/// (it disposes it when it is <see cref="IDisposable"/>).
/// </summary>
internal sealed record AppServices(
    IProcessSnapshotSource Snapshots,
    IProcessInfoSource Info,
    IIconLoader Icons,
    nint GenericIcon,
    ISampleTimer Timer,
    SettingsStore Store,
    LaunchOptions Options)
{
    /// <summary>The real system sources. Call on the UI thread: the sample timer ticks on the thread that creates it.</summary>
    public static AppServices CreateReal(LaunchOptions o)
    {
        var generic = ShellIconLoader.LoadGenericIcon();
        if (generic == 0) generic = User32.CopyIcon(SystemIcons.Application.Handle);
        if (generic == 0) throw new InvalidOperationException("tmcs could not load the generic application icon, from the shell or from the system.");

        return new AppServices(
            new SystemProcessReader(),
            new ProcessInfoSource(new SidNameCache(SidNameCache.LookupAccountName)),
            new ShellIconLoader(),
            generic,
            new WinFormsSampleTimer(),
            new SettingsStore(SettingsStore.DefaultPath),
            o);
    }
}
