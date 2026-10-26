namespace Tmcs.Performance.Views;

/// <summary>
/// Measures and draws the Performance tab's text through <see cref="TextRenderer"/>: GDI ClearType, as Task Manager draws it,
/// so the widths match the captures (look reference section 5).
/// </summary>
internal static class PaneText
{
    const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                                  TextFormatFlags.PreserveGraphicsClipping;

    /// <summary>TextRenderer ignores a Graphics transform unless told, and panes paint with the scroll offset as one.</summary>
    const TextFormatFlags DrawFlags = Flags | TextFormatFlags.PreserveGraphicsTranslateTransform;

    /// <summary>Tall enough for any line; the text is single-line, and a clipped draw needs a rectangle.</summary>
    const int LineBox = 0x4000;

    static readonly Size Unbounded = new(int.MaxValue, int.MaxValue);

    public static readonly Color Black = Color.FromArgb(0x00, 0x00, 0x00);
    public static readonly Color Gray = Color.FromArgb(0x70, 0x70, 0x70);

    public static int Width(string text, Font font) => TextRenderer.MeasureText(text, font, Unbounded, Flags).Width;

    /// <summary>
    /// Draws <paramref name="text"/> with its top left at <paramref name="origin"/>. A finite <paramref name="maxWidth"/> clips
    /// it with an end ellipsis.
    /// </summary>
    public static void Draw(Graphics g, string text, Font font, Point origin, Color color, int maxWidth = int.MaxValue)
    {
        if (maxWidth == int.MaxValue)
            TextRenderer.DrawText(g, text, font, origin, color, DrawFlags);
        else if (maxWidth > 0)
            TextRenderer.DrawText(g, text, font, new Rectangle(origin.X, origin.Y, maxWidth, LineBox), color, DrawFlags | TextFormatFlags.EndEllipsis);
    }
}
