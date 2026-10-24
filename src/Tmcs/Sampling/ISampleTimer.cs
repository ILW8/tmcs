namespace Tmcs.Sampling;

/// <summary>A repeating timer the sample clock drives. Tests substitute a hand-fired fake.</summary>
internal interface ISampleTimer
{
    /// <summary>Milliseconds between <see cref="Tick"/> events.</summary>
    int IntervalMs { get; set; }

    /// <summary>While false, no <see cref="Tick"/> is raised.</summary>
    bool Enabled { get; set; }

    event EventHandler? Tick;
}
