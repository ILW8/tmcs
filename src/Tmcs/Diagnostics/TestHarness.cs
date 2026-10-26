using System.Diagnostics;
using Tmcs.Native;
using Tmcs.Processes;
using Tmcs.Sampling;

namespace Tmcs.Diagnostics;

/// <summary>
/// Test mode's measuring side: the diagnostics log and, when a scenario was given, the runner that plays it on the form. Each has
/// its own timer, apart from the sample clock, so both keep running while the clock is paused. The form creates this only
/// in test mode, starts it once it has shown, and disposes it with itself. When the scenario ends, the form is closed.
/// </summary>
internal sealed class TestHarness : IDisposable
{
    private readonly MainForm _form;
    private readonly IProcessInfoSource _info;
    private readonly WinFormsSampleTimer _diagTimer = new();
    private readonly DiagLog _log;
    private readonly Process _self;
    private readonly WinFormsSampleTimer? _stepTimer;
    private readonly ScenarioRunner? _runner;

    /// <param name="form">Probed for its counters, driven by the scenario, and closed when the scenario ends.</param>
    /// <param name="info">Probed for its account-name cache.</param>
    /// <param name="diagPath">The CSV the log writes.</param>
    /// <param name="scenario">The scenario spec, or null to log until the form is closed. Program has already checked that it parses.</param>
    public TestHarness(MainForm form, IProcessInfoSource info, string diagPath, string? scenario)
    {
        _form = form;
        _info = info;
        var steps = scenario is null ? null : ScenarioParser.Parse(scenario);
        _log = new DiagLog(diagPath, Probe, _diagTimer); // ticks only once the message loop runs, by when every field is set
        _self = Process.GetCurrentProcess();
        if (steps is null) return;

        _stepTimer = new WinFormsSampleTimer();
        // Closed from the message loop rather than inside the step timer's tick, since closing disposes that timer.
        _runner = new ScenarioRunner(steps, form, _stepTimer, () => form.BeginInvoke(form.Close));
    }

    /// <summary>Plays the scenario, if there is one.</summary>
    public void Start() => _runner?.Start();

    /// <summary>The diagnostics log's row for now.</summary>
    public DiagSnapshot Probe()
    {
        _self.Refresh();
        var process = Kernel32.GetCurrentProcess();
        return new DiagSnapshot(
            DateTime.UtcNow,
            _runner?.CurrentStep ?? ScenarioRunner.NoStep,
            _form.Clock.SamplesTaken,
            _form.Table.Count,
            _form.Icons.SlotCount,
            _form.Icons.LiveCount,
            _form.Icons.PooledCount,
            _info.SidCacheCount,
            _self.PrivateMemorySize64,
            GC.GetTotalMemory(false),
            _self.HandleCount,
            (int)User32.GetGuiResources(process, User32.GrGdiObjects),
            (int)User32.GetGuiResources(process, User32.GrUserObjects),
            _self.TotalProcessorTime.TotalMilliseconds,
            _runner?.LastSwitchMs ?? 0,
            _form.Performance.Devices.Count,
            _form.Performance.GpuInstances);
    }

    public void Dispose()
    {
        _stepTimer?.Dispose();
        _log.Dispose(); // flushed and closed
        _diagTimer.Dispose();
        _self.Dispose();
    }
}
