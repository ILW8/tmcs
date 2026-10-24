using System.ComponentModel;
using Tmcs.Icons;
using Tmcs.Processes;
using Tmcs.Settings;
using Tmcs.Ui;

namespace Tmcs.Details;

/// <summary>
/// The Details tab: the process list with the owner's columns, sorting, column hide/select/reorder/resize, type-to-jump,
/// and a selection that stays on the same processes across refreshes. Column changes are written to the <see cref="AppSettings"/>
/// passed in, and <see cref="SettingsChanged"/> tells the owner to save them.
/// </summary>
internal sealed class DetailsView : UserControl, ICellSource
{
    private const int HideColumnCommand = 1;
    private const int SelectColumnsCommand = 2;

    private readonly ProcessTable _table;
    private readonly AppSettings _settings;
    private readonly ProcessListView _list;
    private readonly SelectionKeeper _selection;

    /// <summary>The rows on screen, in display order. Reused by every refresh.</summary>
    private readonly List<ProcessRow> _rows = [];

    /// <summary>The column behind each <see cref="ColumnHeader.Index"/>.</summary>
    private readonly List<ColumnDef> _columns = [];

    /// <summary>
    /// The pixel width each column was last given or reported. A width event that matches it is not a user resize: the list
    /// raises one per column whenever it inserts its header items, and for header changes other than width.
    /// </summary>
    private readonly List<int> _widths = [];

    /// <summary>True while <see cref="ApplyColumnSettings"/> replaces the columns, whose width events must not reach the settings.</summary>
    private bool _rebuilding;

    /// <summary>Shows the rows of <paramref name="table"/> with the layout in <paramref name="settings"/>. The caller owns <paramref name="images"/>, the row icons, and keeps them alive as long as this view.</summary>
    public DetailsView(ProcessTable table, NativeImageList images, AppSettings settings)
    {
        _table = table;
        _settings = settings;
        _list = new ProcessListView { Dock = DockStyle.Fill, SharedImageList = images.Handle, CellSource = this };
        _selection = new SelectionKeeper(_list);
        _list.RetrieveVirtualItem += OnRetrieveVirtualItem;
        _list.SearchForVirtualItem += OnSearchForVirtualItem;
        _list.ColumnClick += (_, e) => SortBy(_columns[e.Column].Id);
        _list.ColumnReordered += OnColumnReordered;
        _list.ColumnWidthChanged += OnColumnWidthChanged;
        _list.HeaderContextMenu += OnHeaderContextMenu;
        // The list raises HandleCreated before it inserts its header items, so the arrow goes on once that has finished.
        _list.HandleCreated += (_, _) =>
        {
            if (IsHandleCreated) BeginInvoke(UpdateSortArrow);
        };
        Controls.Add(_list);

        TrackHeaderMenu = (menu, screen) => menu.Track(this, screen);
        ChooseColumns = s =>
        {
            using var dialog = new SelectColumnsDialog(s);
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Selected : null;
        };

        ApplyColumnSettings();
    }

    /// <summary>Raised after a sort, hide, column selection, reorder or resize has changed the settings.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>The rows in display order, as of the last <see cref="RefreshRows"/>.</summary>
    public IReadOnlyList<ProcessRow> DisplayedRows => _rows;

    /// <summary>Shows the header menu and returns the chosen command id, or 0. Tests replace it so no menu opens.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<NativeMenu, Point, int> TrackHeaderMenu { get; set; }

    /// <summary>Shows the Select columns dialog and returns the chosen columns, or null on Cancel. Tests replace it so no dialog opens.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AppSettings, IReadOnlySet<DetailsColumn>?> ChooseColumns { get; set; }

    /// <summary>Copies the table's rows, sorts them per the settings, and keeps the selection on the same processes.</summary>
    public void RefreshRows()
    {
        _selection.Capture(_rows);
        _rows.Clear();
        _rows.AddRange(_table.Rows);
        RowSorter.Sort(_rows, _settings.SortColumn, _settings.SortDescending);
        _list.SetItemCountKeepScroll(_rows.Count);
        _selection.Restore(_rows); // after the count, so rows that moved past the old end can be selected
        _list.Invalidate();
    }

    /// <summary>Sorts by <paramref name="column"/>: ascending for a new column, the other direction for the current one.</summary>
    public void SortBy(DetailsColumn column)
    {
        if (_settings.SortColumn == column)
        {
            _settings.SortDescending = !_settings.SortDescending;
        }
        else
        {
            _settings.SortColumn = column;
            _settings.SortDescending = false;
        }

        UpdateSortArrow();
        RefreshRows();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Hides <paramref name="column"/>. Name cannot be hidden.</summary>
    public void HideColumn(DetailsColumn column)
    {
        if (column == DetailsColumn.Name) return;
        var setting = Setting(column);
        if (!setting.Visible) return;
        setting.Visible = false;
        VisibilityChanged();
    }

    /// <summary>
    /// Rebuilds the columns from the settings: the visible ones in display order, widths scaled from 96 DPI. Name is always the
    /// first column created, because the list draws the row icon in the first column wherever it is displayed.
    /// </summary>
    public void ApplyColumnSettings()
    {
        var order = AppSettings.VisibleInDisplayOrder(_settings);
        _rebuilding = true;
        _list.BeginUpdate();
        try
        {
            _list.Columns.Clear();
            _columns.Clear();
            _widths.Clear();
            AddColumn(DetailsColumn.Name);
            foreach (var id in order)
                if (id != DetailsColumn.Name)
                    AddColumn(id);

            // Only now: a DisplayIndex beyond the columns added so far throws. Setting them left to right gives the saved order.
            for (var position = 0; position < order.Count; position++)
            {
                var header = _list.Columns[IndexOf(order[position])];
                if (header.DisplayIndex != position) header.DisplayIndex = position;
            }
        }
        finally
        {
            _list.EndUpdate();
            _rebuilding = false;
        }

        UpdateSortArrow();
    }

    private void AddColumn(DetailsColumn id)
    {
        var def = DetailsColumns.Get(id);
        var width = ToPixels(Setting(id).Width);
        _columns.Add(def);
        _widths.Add(width);
        _list.Columns.Add(new ColumnHeader { Text = def.Title, Width = width, TextAlign = def.Align });
    }

    private void UpdateSortArrow() => _list.SetSortArrow(IndexOf(_settings.SortColumn), _settings.SortDescending);

    /// <summary>The <see cref="ColumnHeader.Index"/> of <paramref name="id"/>, or -1 when it is hidden.</summary>
    private int IndexOf(DetailsColumn id)
    {
        for (var i = 0; i < _columns.Count; i++)
            if (_columns[i].Id == id)
                return i;
        return -1;
    }

    private ColumnSetting Setting(DetailsColumn id)
    {
        foreach (var setting in _settings.DetailsColumns)
            if (setting.Id == id)
                return setting;
        throw new InvalidOperationException($"The settings have no entry for column {id}.");
    }

    /// <summary>After columns were shown or hidden: keeps the settings dense, moves the sort off a hidden column, and rebuilds.</summary>
    private void VisibilityChanged()
    {
        var resort = !Setting(_settings.SortColumn).Visible;
        _settings.Normalize(); // also moves the sort off a hidden column, to Name ascending
        ApplyColumnSettings();
        if (resort) RefreshRows();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private int ToPixels(int width) => (int)Math.Round(width * DeviceDpi / 96.0, MidpointRounding.AwayFromZero);

    private int ToUnits(int pixels) => (int)Math.Round(pixels * 96.0 / DeviceDpi, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The list draws its cells through <see cref="ICellSource"/>. This builds the whole row, from the same formatters, for the rare
    /// WinForms paths that still read an item: <c>Items[i]</c>, <see cref="ListView.FocusedItem"/>, <see cref="ListView.TopItem"/>,
    /// accessibility.
    /// </summary>
    private void OnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        var row = _rows[e.ItemIndex];
        var cells = new string[_columns.Count];
        for (var c = 0; c < cells.Length; c++) cells[c] = _columns[c].Text(row);
        e.Item = new ListViewItem(cells, row.IconSlot);
    }

    bool ICellSource.TryWriteCell(int item, int subItem, Span<char> destination, out int written)
    {
        if ((uint)item < (uint)_rows.Count && (uint)subItem < (uint)_columns.Count)
            return _columns[subItem].Format(_rows[item], destination, out written);
        written = 0;
        return true;
    }

    int ICellSource.ImageIndex(int item) => (uint)item < (uint)_rows.Count ? _rows[item].IconSlot : -1;

    private void OnSearchForVirtualItem(object? sender, SearchForVirtualItemEventArgs e)
    {
        if (e.IsTextSearch && e.Text is { } text) e.Index = RowSearch.FindByPrefix(_rows, text, e.StartIndex);
    }

    private void OnColumnWidthChanged(object? sender, ColumnWidthChangedEventArgs e)
    {
        var index = e.ColumnIndex;
        if (_rebuilding || (uint)index >= (uint)_columns.Count) return;
        var pixels = _list.Columns[index].Width;
        if (pixels == _widths[index]) return;
        _widths[index] = pixels;
        Setting(_columns[index].Id).Width = ToUnits(pixels);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raised before the list moves the column, so the headers still hold the old display order. WinForms raises it only for a
    /// real move: not for a column dropped where it was, nor for a drop outside the header.
    /// </summary>
    private void OnColumnReordered(object? sender, ColumnReorderedEventArgs e)
    {
        var count = _columns.Count;
        var order = new DetailsColumn[count];
        for (var i = 0; i < count; i++) order[_list.Columns[i].DisplayIndex] = _columns[i].Id;
        var moved = order[e.OldDisplayIndex];
        if (e.NewDisplayIndex > e.OldDisplayIndex) Array.Copy(order, e.OldDisplayIndex + 1, order, e.OldDisplayIndex, e.NewDisplayIndex - e.OldDisplayIndex);
        else Array.Copy(order, e.NewDisplayIndex, order, e.NewDisplayIndex + 1, e.OldDisplayIndex - e.NewDisplayIndex);
        order[e.NewDisplayIndex] = moved;

        // Visible columns take 0..count-1; the hidden ones already sit after them.
        for (var position = 0; position < count; position++) Setting(order[position]).DisplayIndex = position;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHeaderContextMenu(int column, Point screen)
    {
        DetailsColumn? target = (uint)column < (uint)_columns.Count ? _columns[column].Id : null;
        int command;
        using (var menu = NativeMenu.Popup(("Hide column", HideColumnCommand), ("Select columns", SelectColumnsCommand)))
        {
            menu.SetEnabled(HideColumnCommand, target is { } c && c != DetailsColumn.Name);
            command = TrackHeaderMenu(menu, screen);
        }

        if (command == HideColumnCommand && target is { } hide) HideColumn(hide);
        else if (command == SelectColumnsCommand) SelectColumns();
    }

    private void SelectColumns()
    {
        if (ChooseColumns(_settings) is not { } selected) return;
        var changed = false;
        foreach (var setting in _settings.DetailsColumns)
        {
            var visible = setting.Id == DetailsColumn.Name || selected.Contains(setting.Id);
            if (setting.Visible == visible) continue;
            setting.Visible = visible;
            changed = true;
        }

        if (changed) VisibilityChanged();
    }
}
