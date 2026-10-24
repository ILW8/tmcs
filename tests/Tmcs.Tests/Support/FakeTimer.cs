using Tmcs.Sampling;

namespace Tmcs.Tests.Support;

/// <summary>
/// Timer the test drives by hand. <see cref="Fire"/> raises <see cref="Tick"/> even while <see cref="Enabled"/> is false,
/// which a real timer never does, so tests can show that a consumer ignores ticks it should not get.
/// </summary>
internal sealed class FakeTimer : ISampleTimer
{
    public int IntervalMs { get; set; }
    public bool Enabled { get; set; }
    public event EventHandler? Tick;

    /// <summary>Raises <see cref="Tick"/> once, whatever <see cref="Enabled"/> says.</summary>
    public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
}
