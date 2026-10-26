using Tmcs.Actions;

namespace Tmcs.Tests.Actions;

/// <summary>
/// The Processor affinity dialog (reference 5.1). The model tests create the dialog's handle without showing it, so the checkbox
/// list runs its real notifications; the one test that shows dialogs does so on <see cref="HiddenDesktop"/> and closes each at once.
/// </summary>
public class ProcessorAffinityDialogTests
{
    static ProcessorAffinityDialog Make(nuint systemMask = 0xFF, nuint processMask = 0xFF)
    {
        var dialog = new ProcessorAffinityDialog("notepad.exe", new AffinityInfo(processMask, systemMask));
        // The list's notifications run once it has a window. Nothing is shown.
        _ = dialog.Handle;
        _ = dialog.List.Handle;
        return dialog;
    }

    static List<bool> Checks(ProcessorAffinityDialog d) => d.List.Items.Cast<ListViewItem>().Select(i => i.Checked).ToList();

    [Fact]
    public void Lists_all_processors_then_each_cpu() => Sta.Run(() =>
    {
        using var dialog = Make(0xFF);

        Assert.Equal(
            ["<All Processors>", "CPU 0", "CPU 1", "CPU 2", "CPU 3", "CPU 4", "CPU 5", "CPU 6", "CPU 7"],
            dialog.List.Items.Cast<ListViewItem>().Select(i => i.Text));
    });

    [Fact]
    public void Only_cpus_in_the_system_mask_are_listed_by_bit_index() => Sta.Run(() =>
    {
        using var dialog = Make(systemMask: 0b101010, processMask: 0b001000);

        Assert.Equal(["<All Processors>", "CPU 1", "CPU 3", "CPU 5"], dialog.List.Items.Cast<ListViewItem>().Select(i => i.Text));
        Assert.Equal([false, false, true, false], Checks(dialog));
        Assert.Equal((nuint)0b1000, dialog.Mask);
    });

    [Fact]
    public void List_is_a_checkbox_report_without_header_and_the_text_is_the_reference_s() => Sta.Run(() =>
    {
        using var dialog = Make();

        Assert.Equal("Processor affinity", dialog.Text);
        Assert.Equal("Which processors are allowed to run \"notepad.exe\"?", dialog.Prompt.Text);
        Assert.Equal(View.Details, dialog.List.View);
        Assert.True(dialog.List.CheckBoxes);
        Assert.Equal(ColumnHeaderStyle.None, dialog.List.HeaderStyle);
        Assert.False(dialog.List.MultiSelect);
        Assert.False(dialog.List.HideSelection);
        Assert.False(dialog.List.LabelWrap);
        Assert.Single(dialog.List.Columns);
        Assert.Equal("OK", dialog.OkButton.Text);
        Assert.Same(dialog.OkButton, dialog.AcceptButton);
        Assert.Equal(DialogResult.OK, dialog.OkButton.DialogResult);
        Assert.Equal(DialogResult.Cancel, ((Button)dialog.CancelButton!).DialogResult);
    });

    [Fact]
    public void All_processors_toggles_every_cpu() => Sta.Run(() =>
    {
        using var dialog = Make(0xFF, processMask: 0b11);
        var items = dialog.List.Items;

        items[0].Checked = true;
        Assert.All(Checks(dialog), c => Assert.True(c));
        Assert.Equal((nuint)0xFF, dialog.Mask);

        items[0].Checked = false;
        Assert.All(Checks(dialog), c => Assert.False(c));
        Assert.Equal((nuint)0, dialog.Mask);

        // Checking every CPU one by one checks <All Processors> with the last.
        for (var i = 1; i <= 8; i++)
        {
            Assert.False(items[0].Checked);
            items[i].Checked = true;
        }
        Assert.True(items[0].Checked);

        // Unchecking any CPU unchecks it again, and leaves the other CPUs alone.
        items[4].Checked = false;
        Assert.False(items[0].Checked);
        Assert.Equal(7, Checks(dialog).Skip(1).Count(c => c));
    });

    [Fact]
    public void Ok_is_disabled_with_nothing_checked() => Sta.Run(() =>
    {
        using var dialog = Make(0xFF, processMask: 0b100);
        Assert.True(dialog.OkButton.Enabled);

        dialog.List.Items[3].Checked = false; // CPU 2, the only one
        Assert.False(dialog.OkButton.Enabled);

        dialog.List.Items[1].Checked = true;
        Assert.True(dialog.OkButton.Enabled);

        dialog.List.Items[0].Checked = true; // checks every CPU
        Assert.True(dialog.OkButton.Enabled);

        dialog.List.Items[0].Checked = false; // clears every CPU
        Assert.False(dialog.OkButton.Enabled);
    });

    [Fact]
    public void Ok_is_disabled_when_the_seed_checks_no_listed_cpu() => Sta.Run(() =>
    {
        using var dialog = Make(0b11, processMask: 0b100);
        Assert.False(dialog.OkButton.Enabled);
    });

    [Fact]
    public void Seeded_mask_checks_the_current_cpus() => Sta.Run(() =>
    {
        using var dialog = Make(0xFF, processMask: 0b1010);

        Assert.Equal([false, false, true, false, true, false, false, false, false], Checks(dialog));
        Assert.Equal((nuint)0b1010, dialog.Mask);
    });

    [Fact]
    public void Seeding_every_cpu_checks_all_processors() => Sta.Run(() =>
    {
        using var dialog = Make(0b1111, processMask: 0b1111);
        Assert.All(Checks(dialog), c => Assert.True(c));
    });

    [Fact]
    public void Chosen_mask_reflects_the_checks() => Sta.Run(() =>
    {
        using var dialog = Make(0xFF, processMask: 0xFF);
        dialog.List.Items[0].Checked = false;

        dialog.List.Items[1].Checked = true; // CPU 0
        dialog.List.Items[3].Checked = true; // CPU 2

        Assert.Equal((nuint)0b101, dialog.Mask);
    });

    [Fact]
    public void Column_fills_the_list_so_every_row_has_a_label() => Sta.Run(() =>
    {
        using var dialog = Make(0xFF);

        Assert.True(dialog.List.ClientSize.Width > 0);
        Assert.Equal(dialog.List.ClientSize.Width, dialog.List.Columns[0].Width);
        foreach (ListViewItem item in dialog.List.Items) Assert.True(item.Bounds.Width > 0, $"{item.Text} has no width");
    });

    [Fact]
    public void Column_follows_the_client_width_when_a_scroll_bar_appears()
    {
        int clientWidth = 0, columnWidth = 0, rows = 0;
        bool scrolls = false;
        var emptyRows = new List<string>();
        HiddenDesktop.Run(() =>
        {
            // 32 CPUs and the all-row are taller than the list, so it has a vertical scroll bar.
            using var dialog = new ProcessorAffinityDialog("notepad.exe", new AffinityInfo(uint.MaxValue, uint.MaxValue));
            dialog.Shown += (_, _) => dialog.BeginInvoke(() =>
            {
                clientWidth = dialog.List.ClientSize.Width;
                columnWidth = dialog.List.Columns[0].Width;
                rows = dialog.List.Items.Count;
                scrolls = dialog.List.ClientSize.Width < dialog.List.Width - 2 * SystemInformation.Border3DSize.Width;
                foreach (ListViewItem item in dialog.List.Items)
                    if (item.Bounds.Width <= 0) emptyRows.Add(item.Text);
                dialog.DialogResult = DialogResult.Cancel;
            });
            dialog.ShowDialog();
        });

        Assert.Equal(33, rows);
        Assert.True(scrolls, "the list has no scroll bar");
        Assert.Equal(clientWidth, columnWidth);
        Assert.Empty(emptyRows);
    }

    [Fact]
    public void Thousand_open_and_cancel_stay_flat()
    {
        (int Gdi, int User, int Handles) first = default, last = default;
        HiddenDesktop.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                using var dialog = new ProcessorAffinityDialog("notepad.exe", new AffinityInfo(0xFF, 0xFF));
                // Closed from the first message the shown dialog pumps, never on a timer.
                dialog.Shown += (_, _) => dialog.BeginInvoke(() => dialog.DialogResult = DialogResult.Cancel);
                Assert.Equal(DialogResult.Cancel, dialog.ShowDialog());
                if (i != 0) continue;
                // Garbage from earlier tests is collected before the baseline, so its finalizers do not lower the count mid-loop.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                first = TaskDialogDriver.Counts();
            }
            last = TaskDialogDriver.Counts();
        });

        TaskDialogDriver.AssertFlat(first, last);
    }

    [Fact]
    public void Shown_dialog_answers_ok_with_its_mask_and_cancel_without()
    {
        DialogResult ok = DialogResult.None, cancel = DialogResult.None;
        nuint mask = 0;
        HiddenDesktop.Run(() =>
        {
            using (var dialog = new ProcessorAffinityDialog("notepad.exe", new AffinityInfo(0b0110, 0b1111)))
            {
                dialog.Shown += (_, _) => dialog.BeginInvoke(() => dialog.OkButton.PerformClick());
                ok = dialog.ShowDialog();
                mask = dialog.Mask;
            }
            using (var dialog = new ProcessorAffinityDialog("notepad.exe", new AffinityInfo(0b0110, 0b1111)))
            {
                dialog.Shown += (_, _) => dialog.BeginInvoke(() => ((Button)dialog.CancelButton!).PerformClick());
                cancel = dialog.ShowDialog();
            }
        });

        Assert.Equal(DialogResult.OK, ok);
        Assert.Equal((nuint)0b0110, mask);
        Assert.Equal(DialogResult.Cancel, cancel);
    }
}
