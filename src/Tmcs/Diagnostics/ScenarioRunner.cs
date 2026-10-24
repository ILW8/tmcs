using System.Diagnostics;
using Tmcs.Sampling;

namespace Tmcs.Diagnostics;

/// <summary>What a scenario drives: the main window in test mode.</summary>
internal interface IScenarioTarget
{
    /// <summary>Puts tmcs fully into the state <paramref name="a"/> names, and returns only when it is there.</summary>
    void Apply(ScenarioAction a);
}

/// <summary>
/// Plays a scenario: <see cref="Start"/> applies the first step, and each tick of the step timer, which is set to the running
/// step's duration, applies the next. After the last step the timer stops and <c>finished</c> is called. Ticks before
/// <see cref="Start"/> or after the end are ignored.
/// </summary>
internal sealed class ScenarioRunner
{
    /// <summary>The <see cref="CurrentStep"/> before <see cref="Start"/>.</summary>
    public const string NoStep = "none";

    /// <summary>The <see cref="CurrentStep"/> after the last step.</summary>
    public const string DoneStep = "done";

    private readonly IReadOnlyList<ScenarioStep> _steps;
    private readonly IScenarioTarget _target;
    private readonly ISampleTimer _timer;
    private readonly Action _finished;

    /// <summary>The running step; -1 before <see cref="Start"/>, the step count after the end.</summary>
    private int _index = -1;

    public ScenarioRunner(IReadOnlyList<ScenarioStep> steps, IScenarioTarget target, ISampleTimer stepTimer, Action finished)
    {
        _steps = steps;
        _target = target;
        _timer = stepTimer;
        _finished = finished;
        _timer.Enabled = false;
        _timer.Tick += OnTick;
    }

    /// <summary>The running step's action name, lowercase; <see cref="NoStep"/> before <see cref="Start"/>; <see cref="DoneStep"/> after the end.</summary>
    public string CurrentStep =>
        _index < 0 ? NoStep
        : _index < _steps.Count ? ScenarioParser.Name(_steps[_index].Action)
        : DoneStep;

    /// <summary>How long the latest step's <see cref="IScenarioTarget.Apply"/> took, in milliseconds; 0 before the first.</summary>
    public double LastSwitchMs { get; private set; }

    /// <exception cref="InvalidOperationException">The scenario has already started.</exception>
    public void Start()
    {
        if (_index >= 0) throw new InvalidOperationException("The scenario has already started.");
        Advance();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_index >= 0 && _index < _steps.Count) Advance();
    }

    private void Advance()
    {
        _timer.Enabled = false;
        _index++;
        if (_index == _steps.Count)
        {
            _finished(); // last: it may dispose the timer
            return;
        }

        var step = _steps[_index];
        var start = Stopwatch.GetTimestamp();
        _target.Apply(step.Action);
        LastSwitchMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // The parser keeps a step within int.MaxValue ms. A timer needs at least 1 ms.
        _timer.IntervalMs = (int)Math.Max(1, Math.Ceiling(step.Duration.TotalMilliseconds));
        _timer.Enabled = true;
    }
}
