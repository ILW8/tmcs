namespace Tmcs.Details;

/// <summary>
/// Gives <see cref="ProcessListView"/> its cells one at a time, written straight into the control's own buffer, so drawing a row
/// allocates nothing. Indexes are the list's: <c>item</c> is the row in display order, <c>subItem</c> the column's
/// <see cref="ColumnHeader.Index"/>.
/// </summary>
internal interface ICellSource
{
    /// <summary>
    /// Writes the text of one cell into <paramref name="destination"/>, cut to the prefix that fits. A row or column that does not
    /// exist writes nothing.
    /// </summary>
    /// <returns>False when the text was cut.</returns>
    bool TryWriteCell(int item, int subItem, Span<char> destination, out int written);

    /// <summary>The row's index in the list's image list, or -1 for none, as for a row that does not exist.</summary>
    int ImageIndex(int item);
}
