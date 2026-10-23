namespace Tmcs.Sampling;

/// <summary>
/// The one clock that drives every sample. Each speed maps to a timer interval; <see cref="UpdateSpeed.Paused"/> disables the
/// timer outright, so a paused clock takes no samples. <see cref="RefreshNow"/> takes exactly one sample in any state.
/// </summary>
internal sealed class SampleClock
{
    private readonly ISampleTimer _timer;
    private UpdateSpeed _speed = UpdateSpeed.Normal;

    /// <summary>Puts <paramref name="timer"/> into the state for the default speed, <see cref="UpdateSpeed.Normal"/>.</summary>
    public SampleClock(ISampleTimer timer)
    {
        _timer = timer;
        _timer.Tick += OnTick;
        Apply(_speed);
    }

    /// <summary>Raised once per sample: on each timer tick while running, and on each <see cref="RefreshNow"/>.</summary>
    public event Action? Sample;

    /// <summary>Every <see cref="Sample"/> raised so far.</summary>
    public long SamplesTaken { get; private set; }

    /// <summary>Setting it reconfigures the timer at once. It never takes a sample.</summary>
    public UpdateSpeed Speed
    {
        get => _speed;
        set
        {
            Apply(value);
            _speed = value;
        }
    }

    /// <summary>The Task Manager interval for <paramref name="speed"/>. Throws for <see cref="UpdateSpeed.Paused"/>, which has none.</summary>
    public static int IntervalFor(UpdateSpeed speed) => speed switch
    {
        UpdateSpeed.High => 500,
        UpdateSpeed.Normal => 1000,
        UpdateSpeed.Low => 4000,
        _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, "This speed has no sampling interval."),
    };

    /// <summary>Takes one sample now, running or paused. The timer is left as it is.</summary>
    public void RefreshNow() => RaiseSample();

    // Works out the interval before touching the timer, so an invalid speed throws and leaves the timer as it was.
    private void Apply(UpdateSpeed speed)
    {
        if (speed == UpdateSpeed.Paused)
        {
            _timer.Enabled = false;
            return;
        }

        _timer.IntervalMs = IntervalFor(speed);
        _timer.Enabled = true;
    }

    // Belt and braces: a disabled timer should never tick, but a tick already queued when Paused was set must not sample.
    private void OnTick(object? sender, EventArgs e)
    {
        if (_speed != UpdateSpeed.Paused) RaiseSample();
    }

    private void RaiseSample()
    {
        SamplesTaken++;
        Sample?.Invoke();
    }
}
