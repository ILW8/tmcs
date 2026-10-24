using Tmcs.Processes;

namespace Tmcs.Details;

/// <summary>Writes one cell's text into <paramref name="destination"/>, cut to the prefix that fits.</summary>
/// <returns>False when the text was cut.</returns>
internal delegate bool CellFormatter(ProcessRow row, Span<char> destination, out int written);

/// <summary>One Details column: its header, its default layout, the cell text, and how rows order by it.</summary>
/// <param name="Id">The column this describes.</param>
/// <param name="Title">The header text.</param>
/// <param name="DefaultWidth">Pixels at 96 DPI.</param>
/// <param name="DefaultVisible">Whether the column is shown until the user hides it.</param>
/// <param name="Align">Text alignment for the header and the cells.</param>
/// <param name="Format">Writes the cell text. The list draws cells with it; <see cref="Text"/> is built from it.</param>
/// <param name="Compare">Compares the underlying values, not the formatted text. It applies no tie-breaker; <see cref="RowSorter"/> does.</param>
internal sealed record ColumnDef(
    DetailsColumn Id,
    string Title,
    int DefaultWidth,
    bool DefaultVisible,
    HorizontalAlignment Align,
    CellFormatter Format,
    Comparison<ProcessRow> Compare)
{
    /// <summary>The cell text as a string, for the callers that need one. Drawing uses <see cref="Format"/>, which allocates nothing.</summary>
    public string Text(ProcessRow row)
    {
        Span<char> small = stackalloc char[256];
        if (Format(row, small, out var written)) return new string(small[..written]);

        // Only command lines run longer: up to 32,767 characters.
        for (var size = 4096;; size *= 2)
        {
            var large = new char[size];
            if (Format(row, large, out written)) return new string(large, 0, written);
        }
    }
}
