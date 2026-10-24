using Tmcs.Details;
using Tmcs.Sampling;
using Columns = Tmcs.Details.DetailsColumns;

namespace Tmcs.Settings;

/// <summary>Everything tmcs remembers between runs. The defaults are the owner's Task Manager layout. Get/set properties because System.Text.Json fills them.</summary>
internal sealed class AppSettings
{
    const int MinColumnWidth = 20;

    /// <summary>Top-left of the restored window. Null until the window has been saved once, so Windows picks the position.</summary>
    public int? WindowX { get; set; }

    public int? WindowY { get; set; }

    /// <summary>Width of the restored window in pixels, frame included. <see cref="WindowHeight"/> likewise.</summary>
    public int WindowWidth { get; set; } = 1294;

    public int WindowHeight { get; set; } = 712;

    public bool Maximized { get; set; }

    public bool AlwaysOnTop { get; set; }

    public UpdateSpeed UpdateSpeed { get; set; } = UpdateSpeed.Normal;

    public List<ColumnSetting> DetailsColumns { get; set; } = [];

    public DetailsColumn SortColumn { get; set; } = DetailsColumn.Name;

    public bool SortDescending { get; set; }

    /// <summary>Unused until M2.</summary>
    public List<string> HiddenAdapters { get; set; } = [];

    /// <summary>The owner's Task Manager layout: the 11 columns at their default widths, the first eight visible, in enum order.</summary>
    public static AppSettings CreateDefault() => new()
    {
        DetailsColumns = [.. Columns.All.Select(static (def, i) => new ColumnSetting
        {
            Id = def.Id,
            Visible = def.DefaultVisible,
            Width = def.DefaultWidth,
            DisplayIndex = i,
        })],
    };

    /// <summary>Repairs a loaded or hand-edited file so the rest of the app can trust it. A second call changes nothing.</summary>
    public void Normalize()
    {
        // System.Text.Json sets a property to null when the file says null, whatever the annotations promise, so the lists are checked for null despite their annotations.
        // ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        // ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        var seen = new HashSet<DetailsColumn>();
        var columns = new List<ColumnSetting>(Columns.All.Count);
        foreach (var c in DetailsColumns ?? [])
        {
            if (c is not null && Enum.IsDefined(c.Id) && seen.Add(c.Id)) columns.Add(c);
        }

        // Columns the file does not know yet go after everything else, in enum order (the sort below is stable).
        foreach (var def in Columns.All)
        {
            if (seen.Add(def.Id)) columns.Add(new ColumnSetting { Id = def.Id, Visible = def.DefaultVisible, Width = def.DefaultWidth, DisplayIndex = int.MaxValue });
        }

        foreach (var c in columns)
        {
            c.Width = Math.Max(MinColumnWidth, c.Width);
            if (c.Id == DetailsColumn.Name) c.Visible = true;
        }

        columns = [.. columns.OrderBy(static c => c.Visible ? 0 : 1).ThenBy(static c => c.DisplayIndex)];
        for (var i = 0; i < columns.Count; i++) columns[i].DisplayIndex = i;
        DetailsColumns = columns;

        // A sort column that is unknown or hidden goes back to the default sort, Name ascending.
        if (!Enum.IsDefined(SortColumn) || !columns.Exists(c => c.Id == SortColumn && c.Visible))
        {
            SortColumn = DetailsColumn.Name;
            SortDescending = false;
        }

        if (!Enum.IsDefined(UpdateSpeed)) UpdateSpeed = UpdateSpeed.Normal;
        HiddenAdapters ??= [];
        // ReSharper restore ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        // ReSharper restore NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
    }

    /// <summary>The visible columns, left to right.</summary>
    public static IReadOnlyList<DetailsColumn> VisibleInDisplayOrder(AppSettings s) =>
        [.. s.DetailsColumns.Where(static c => c.Visible).OrderBy(static c => c.DisplayIndex).Select(static c => c.Id)];
}
