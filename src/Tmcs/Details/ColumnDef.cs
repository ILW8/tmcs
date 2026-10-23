using Tmcs.Processes;

namespace Tmcs.Details;

/// <summary>One Details column: its header, its default layout, the cell text, and how rows order by it.</summary>
/// <param name="Id">The column this describes.</param>
/// <param name="Title">The header text.</param>
/// <param name="DefaultWidth">Pixels at 96 DPI.</param>
/// <param name="DefaultVisible">Whether the column is shown until the user hides it.</param>
/// <param name="Align">Text alignment for the header and the cells.</param>
/// <param name="Text">The cell text.</param>
/// <param name="Compare">Compares the underlying values, not the formatted text. It applies no tie-breaker; <see cref="RowSorter"/> does.</param>
internal sealed record ColumnDef(
    DetailsColumn Id,
    string Title,
    int DefaultWidth,
    bool DefaultVisible,
    HorizontalAlignment Align,
    Func<ProcessRow, string> Text,
    Comparison<ProcessRow> Compare);
