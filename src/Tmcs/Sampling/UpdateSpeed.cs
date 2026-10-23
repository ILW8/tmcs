namespace Tmcs.Sampling;

/// <summary>How often the process table is sampled. The sample clock maps each value to an interval.</summary>
internal enum UpdateSpeed
{
    High,
    Normal,
    Low,
    Paused,
}
