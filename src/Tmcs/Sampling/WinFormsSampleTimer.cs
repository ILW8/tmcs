namespace Tmcs.Sampling;

/// <summary>An <see cref="ISampleTimer"/> over <see cref="System.Windows.Forms.Timer"/>. Ticks arrive on the thread that created it.</summary>
internal sealed class WinFormsSampleTimer : ISampleTimer, IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new();

    public WinFormsSampleTimer() => _timer.Tick += OnTick;

    public int IntervalMs
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public bool Enabled
    {
        get => _timer.Enabled;
        set => _timer.Enabled = value;
    }

    public event EventHandler? Tick;

    private void OnTick(object? sender, EventArgs e) => Tick?.Invoke(this, e);

    public void Dispose()
    {
        _timer.Tick -= OnTick;
        _timer.Dispose();
    }
}
