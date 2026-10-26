namespace Tmcs.Performance.Views;

/// <summary>
/// The Performance tab's text styles at one DPI: Segoe UI Regular at Task Manager's pixel sizes (look reference section 5),
/// scaled by <c>dpi / 96</c>. Created once per DPI; the view replaces them on a DPI change, never per paint. The sidebar uses
/// <see cref="Subtitle"/> for entry titles and <see cref="Label"/> for their sub lines.
/// </summary>
internal sealed class PaneFonts(int dpi) : IDisposable
{
    public int Dpi { get; } = dpi;

    /// <summary>The pane title (<c>CPU</c>), 28 px.</summary>
    public Font Title { get; } = Create(28, dpi);

    /// <summary>The pane subtitle (the device name), 16 px.</summary>
    public Font Subtitle { get; } = Create(16, dpi);

    /// <summary>Graph captions, scale labels and GPU engine percentages, 11 px.</summary>
    public Font Caption { get; } = Create(11, dpi);

    /// <summary>The labels above big values and the static table, 12 px.</summary>
    public Font Label { get; } = Create(12, dpi);

    /// <summary>The big values, 20 px.</summary>
    public Font Value { get; } = Create(20, dpi);

    /// <summary>The GPU engine names, 13 px.</summary>
    public Font Engine { get; } = Create(13, dpi);

    /// <summary>A length in 96-DPI units, in pixels at this DPI.</summary>
    public int Scale(int logical) => (int)Math.Round(logical * Dpi / 96.0, MidpointRounding.AwayFromZero);

    public void Dispose()
    {
        Title.Dispose();
        Subtitle.Dispose();
        Caption.Dispose();
        Label.Dispose();
        Value.Dispose();
        Engine.Dispose();
    }

    static Font Create(int px, int dpi) => new("Segoe UI", px * dpi / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
}
