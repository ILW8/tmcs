using Tmcs.Settings;

namespace Tmcs.Details;

/// <summary>
/// Task Manager's Select columns dialog, limited to the 11 Details columns. Name is always checked. It changes nothing itself:
/// after OK the caller applies <see cref="Selected"/> to the settings.
/// </summary>
internal sealed class SelectColumnsDialog : Form
{
    private readonly CheckedListBox _columns;

    public SelectColumnsDialog(AppSettings settings)
    {
        var visible = AppSettings.VisibleInDisplayOrder(settings);

        _columns = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        foreach (var def in DetailsColumns.All) _columns.Items.Add(def.Title, def.Id == DetailsColumn.Name || visible.Contains(def.Id));
        _columns.ItemCheck += (_, e) =>
        {
            if (DetailsColumns.All[e.Index].Id == DetailsColumn.Name) e.NewValue = CheckState.Checked;
        };

        var prompt = new Label
        {
            Text = "Select the columns that will appear in the table.",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8),
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        SuspendLayout();
        Text = "Select columns";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(12);
        ClientSize = new Size(300, 360);
        AcceptButton = ok;
        CancelButton = cancel;
        // Docking runs from the last control added to the first, so the list fills what the label and the buttons leave.
        Controls.Add(_columns);
        Controls.Add(buttons);
        Controls.Add(prompt);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>The checked columns, Name always among them.</summary>
    public IReadOnlySet<DetailsColumn> Selected
    {
        get
        {
            var selected = new HashSet<DetailsColumn>();
            foreach (int i in _columns.CheckedIndices) selected.Add(DetailsColumns.All[i].Id);
            return selected;
        }
    }
}
