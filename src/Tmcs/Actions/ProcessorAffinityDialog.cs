namespace Tmcs.Actions;

/// <summary>
/// Task Manager's Processor affinity dialog (reference 5.1, dialog 34701, one processor group): a prompt, a checkbox list with
/// <c>&lt;All Processors&gt;</c> first and then one row per logical processor in the system mask, and OK and Cancel. It changes
/// nothing itself: after OK the caller applies <see cref="Mask"/>. OK is grayed while no processor is checked (reference 5.1 leaves
/// that rule to tmcs).
/// </summary>
internal sealed class ProcessorAffinityDialog : Form
{
    // The dialog is laid out in dialog units of its font, 8 pt MS Shell Dlg (Microsoft Sans Serif), whose units at 96 dpi are
    // 6 x 13 pixels per 4 x 8 units. The form scales the pixels with the DPI, as the other dialogs do.
    const int UnitX = 6;
    const int UnitY = 13;

    // The buttons stay FlatStyle.Standard: a system-style button on a form with an explicit font leaves a GDI object behind
    // per button until the finalizer runs, and ResetFont in Dispose does not free it.
    readonly Font _font = new("Microsoft Sans Serif", 8F);
    readonly ListView _list;
    readonly Button _ok;
    readonly Label _prompt;

    // The bit each CPU row stands for: row i + 1 is CPU _bits[i].
    readonly int[] _bits;

    // Set while a check is changed by this dialog's own rule, so the change does not apply the rule again.
    bool _updating;

    public ProcessorAffinityDialog(string processName, AffinityInfo info)
    {
        _bits = SetBits(info.SystemMask);

        _prompt = new Label
        {
            Text = string.Format(ActionStrings.AffinityPrompt, processName),
            AutoSize = false,
            UseMnemonic = false, // a process name may hold an ampersand
            Bounds = Dlu(7, 4, 188, 19),
        };

        _list = new ListView
        {
            View = View.Details,
            HeaderStyle = ColumnHeaderStyle.None,
            CheckBoxes = true,
            MultiSelect = false,
            HideSelection = false,
            LabelWrap = false,
            Sorting = SortOrder.None,
            BorderStyle = BorderStyle.FixedSingle,
            Name = "Processors",
            Bounds = Dlu(7, 28, 188, 120),
        };
        _list.Columns.Add(string.Empty, 0);
        // The one column fills the list, less the scroll bar when many processors need one. The width is set when the window exists
        // (the client size is final by then at 96 dpi, where nothing rescales the list) and follows every later change of it.
        _list.HandleCreated += (_, _) => FillColumn();
        _list.ClientSizeChanged += (_, _) => FillColumn();

        var all = _list.Items.Add(ActionStrings.AllProcessors);
        var everyCpu = true;
        foreach (var bit in _bits)
        {
            var isChecked = (info.ProcessMask >> bit & 1) != 0;
            everyCpu &= isChecked;
            _list.Items.Add(new ListViewItem(string.Format(ActionStrings.CpuRow, bit)) { Checked = isChecked });
        }
        all.Checked = everyCpu && _bits.Length > 0;
        _list.ItemChecked += OnItemChecked;

        _ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = Dlu(90, 157, 50, 14) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Bounds = Dlu(145, 157, 50, 14) };

        SuspendLayout();
        Text = ActionStrings.AffinityTitle;
        Font = _font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(Dlu(202, UnitX / 4.0), Dlu(178, UnitY / 8.0));
        AcceptButton = _ok;
        CancelButton = cancel;
        Controls.Add(_prompt);
        Controls.Add(_list);
        Controls.Add(_ok);
        Controls.Add(cancel);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();

        UpdateOk();
    }

    protected override void Dispose(bool disposing)
    {
        // A form with an explicit font leaves a GDI object behind per instance (its handle wrapper waits for the finalizer) unless the
        // font is reset first. The form does not own the font it is given, so it is disposed here.
        if (disposing) ResetFont();
        base.Dispose(disposing);
        if (disposing) _font.Dispose();
    }

    /// <summary>The processors that are checked, as a mask. Read it after OK.</summary>
    public nuint Mask
    {
        get
        {
            nuint mask = 0;
            for (var i = 0; i < _bits.Length; i++)
                if (_list.Items[i + 1].Checked)
                    mask |= (nuint)1 << _bits[i];
            return mask;
        }
    }

    /// <summary>The prompt above the list, for tests.</summary>
    internal Label Prompt => _prompt;

    /// <summary>The checkbox list: <c>&lt;All Processors&gt;</c>, then a row per processor. For tests.</summary>
    internal ListView List => _list;

    internal Button OkButton => _ok;

    /// <summary>
    /// &lt;All Processors&gt; checks or clears every processor; any other row sets &lt;All Processors&gt; to whether every processor is
    /// now checked. OK follows the checks.
    /// </summary>
    void OnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            if (e.Item.Index == 0)
            {
                for (var i = 1; i < _list.Items.Count; i++) _list.Items[i].Checked = e.Item.Checked;
            }
            else
            {
                var every = true;
                for (var i = 1; i < _list.Items.Count; i++) every &= _list.Items[i].Checked;
                _list.Items[0].Checked = every;
            }
        }
        finally
        {
            _updating = false;
        }
        UpdateOk();
    }

    void FillColumn()
    {
        var width = _list.ClientSize.Width;
        if (width > 0 && _list.Columns[0].Width != width) _list.Columns[0].Width = width;
    }

    void UpdateOk() => _ok.Enabled = Mask != 0;

    static int[] SetBits(nuint mask)
    {
        var bits = new List<int>();
        for (var bit = 0; bit < nint.Size * 8; bit++)
            if ((mask >> bit & 1) != 0)
                bits.Add(bit);
        return [.. bits];
    }

    static Rectangle Dlu(int x, int y, int width, int height) =>
        new(Dlu(x, UnitX / 4.0), Dlu(y, UnitY / 8.0), Dlu(width, UnitX / 4.0), Dlu(height, UnitY / 8.0));

    static int Dlu(int units, double pixelsPerUnit) => (int)Math.Round(units * pixelsPerUnit, MidpointRounding.AwayFromZero);
}
