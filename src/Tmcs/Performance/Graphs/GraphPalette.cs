namespace Tmcs.Performance.Graphs;

/// <summary>
/// The colors of one kind of graph. A <see cref="SecondFill"/> of <see cref="Color.Empty"/> means the second series is a
/// dashed line with no fill.
/// </summary>
internal sealed record GraphPalette(
    Color Border, Color TopCorner, Color BottomCorner, Color Grid, Color Line, Color Fill, Color GridInFill,
    Color SecondLine, Color SecondFill, Color GridInSecondFill);

/// <summary>Task Manager's graph colors, from the look reference (section 3).</summary>
internal static class GraphPalettes
{
    static Color C(int rgb) => Color.FromArgb(unchecked((int)0xFF000000) | rgb);

    public static readonly GraphPalette Cpu = new(
        C(0x117DBB), C(0x3A94C7), C(0x1F85BF), C(0xD9EAF4), C(0x117DBB), C(0xF1F6FA), C(0xCEE2F0),
        C(0x81B3D2), C(0xD9E7F1), C(0xBAD6E8));

    public static readonly GraphPalette Memory = new(
        C(0x8B12AE), C(0x9F3BBB), C(0x9220B3), C(0xECDEF0), C(0x9528B4), C(0xF4F2F4), C(0xE3D4E6),
        Color.Empty, Color.Empty, Color.Empty);

    public static readonly GraphPalette Disk = new(
        C(0x4DA60C), C(0x6CB536), C(0x58AC1A), C(0xDBEDCE), C(0x4DA60C), C(0xEFF7E9), C(0xCEE7BC),
        C(0x79D50D), Color.Empty, Color.Empty);

    public static readonly GraphPalette Network = new(
        C(0xA74F01), C(0xB66D2D), C(0xAD5A10), C(0xEEDECF), C(0xA74F01), C(0xFCF3EB), C(0xEDD5C0),
        C(0xDF672C), Color.Empty, Color.Empty);

    /// <summary>The CPU colors; the GPU graphs have no second series, so they just never pass one.</summary>
    public static readonly GraphPalette Gpu = Cpu;
}
