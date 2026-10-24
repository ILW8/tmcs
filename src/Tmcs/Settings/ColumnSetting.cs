using Tmcs.Details;

namespace Tmcs.Settings;

/// <summary>The saved layout of one Details column. Get/set properties because System.Text.Json fills them.</summary>
internal sealed class ColumnSetting
{
    public DetailsColumn Id { get; set; }

    public bool Visible { get; set; }

    /// <summary>Pixels at 96 DPI.</summary>
    public int Width { get; set; }

    /// <summary>Left-to-right position among all columns. <see cref="AppSettings.Normalize"/> keeps these dense: visible columns first.</summary>
    public int DisplayIndex { get; set; }
}
