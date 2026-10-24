namespace Tmcs.Performance.Views;

/// <summary>The legend marker drawn left of an item: Disk Read/Write and Network Send/Receive.</summary>
internal enum InfoMarker { None, Solid, Dashed }

/// <summary>A dynamic value: a label over a big value.</summary>
internal readonly record struct InfoItem(string Label, string Value, InfoMarker Marker = InfoMarker.None);

/// <summary>A static table row. <see cref="Height"/> is 19 for the Wi-Fi signal row.</summary>
internal readonly record struct StaticRow(string Key, string Value, int Height = 18);

/// <summary>An item's place: <see cref="X"/> is the marker's (the item's) x, <see cref="TextX"/> its label's and value's.</summary>
internal readonly record struct PlacedItem(InfoItem Item, int X, int Y, int TextX, int Width);

internal readonly record struct PlacedRow(StaticRow Row, int KeyX, int ValueX, int Y);

/// <summary>
/// The geometry of a pane's info block, the dynamic values and the static table beside them, by look reference section 7.
/// Positions are relative to the block's top left and in 96-DPI units, so the width functions must give 96-DPI widths too;
/// the pane scales the result when it paints. Pure arithmetic, so it is tested without a window.
/// </summary>
internal sealed class InfoLayout
{
    /// <summary>Rows (and the items in a GPU column) are this far apart. A value sits 15 px below its label.</summary>
    const int Pitch = 51;

    /// <summary>The last row's value box ends this far above the next pitch.</summary>
    const int LastRowTrim = 8;

    const int ItemGap = 20;
    const int MarkerIndent = 8;
    const int TableGap = 12;
    const int KeyGap = 14;

    InfoLayout(List<PlacedItem> items, int right, int lines, IReadOnlyList<StaticRow> table, Func<string, int> labelWidth)
    {
        Items = items;
        var keyX = right + TableGap;
        var widestKey = 0;
        foreach (var row in table) widestKey = Math.Max(widestKey, labelWidth(row.Key));
        var placed = new PlacedRow[table.Count];
        var y = 0;
        for (var i = 0; i < table.Count; i++)
        {
            placed[i] = new PlacedRow(table[i], keyX, keyX + widestKey + KeyGap, y);
            y += table[i].Height;
        }

        Table = placed;
        Height = Math.Max(lines * Pitch - LastRowTrim, y);
    }

    public int Height { get; }
    public IReadOnlyList<PlacedItem> Items { get; }
    public IReadOnlyList<PlacedRow> Table { get; }

    /// <summary>
    /// CPU, Memory, Disk and Network: each row flows on its own from x 0, so columns don't line up across rows. Labels are in the
    /// Label font (<paramref name="labelWidth"/>, which also measures the table keys), values in the Value font. A marked item
    /// is at least <paramref name="minMarkedWidth"/> wide.
    /// </summary>
    public static InfoLayout Rows(IReadOnlyList<IReadOnlyList<InfoItem>> rows, IReadOnlyList<StaticRow> table,
        Func<string, int> labelWidth, Func<string, int> valueWidth, int minMarkedWidth = 0)
    {
        var items = new List<PlacedItem>();
        var right = 0;
        for (var r = 0; r < rows.Count; r++)
        {
            var x = 0;
            foreach (var item in rows[r])
            {
                var textX = TextX(item, x);
                var width = Width(item, labelWidth, valueWidth, minMarkedWidth);
                items.Add(new PlacedItem(item, x, r * Pitch, textX, width));
                right = Math.Max(right, textX + width);
                x = textX + width + ItemGap;
            }
        }

        return new InfoLayout(items, right, rows.Count, table, labelWidth);
    }

    /// <summary>GPU: each inner list is a column as wide as its widest item, and every item in it gets that width.</summary>
    public static InfoLayout Columns(IReadOnlyList<IReadOnlyList<InfoItem>> columns, IReadOnlyList<StaticRow> table,
        Func<string, int> labelWidth, Func<string, int> valueWidth, int minMarkedWidth = 0)
    {
        var items = new List<PlacedItem>();
        var x = 0;
        var right = 0;
        var longest = 0;
        foreach (var column in columns)
        {
            var width = 0;
            foreach (var item in column) width = Math.Max(width, Width(item, labelWidth, valueWidth, minMarkedWidth));
            var columnRight = x;
            for (var r = 0; r < column.Count; r++)
            {
                var textX = TextX(column[r], x);
                items.Add(new PlacedItem(column[r], x, r * Pitch, textX, width));
                columnRight = Math.Max(columnRight, textX + width);
            }

            right = Math.Max(right, columnRight);
            longest = Math.Max(longest, column.Count);
            x = columnRight + ItemGap;
        }

        return new InfoLayout(items, right, longest, table, labelWidth);
    }

    static int TextX(InfoItem item, int x) => item.Marker == InfoMarker.None ? x : x + MarkerIndent;

    static int Width(InfoItem item, Func<string, int> labelWidth, Func<string, int> valueWidth, int minMarkedWidth)
    {
        var width = Math.Max(labelWidth(item.Label), valueWidth(item.Value));
        return item.Marker == InfoMarker.None ? width : Math.Max(width, minMarkedWidth);
    }
}
