using System.Globalization;
using Tmcs.Details;
using Tmcs.Processes;

namespace Tmcs.Tests.Details;

public class DetailsModelTests : IDisposable
{
    readonly CultureInfo _saved = CultureInfo.CurrentCulture;

    public DetailsModelTests() => CultureInfo.CurrentCulture = new("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _saved;

    static ProcessRow R(int pid, string name, double cpu = 0) =>
        new(new ProcessIdentity(pid, 1), name, 0, ProcessStaticInfo.Blank) { CpuPercent = cpu };

    [Fact]
    public void Columns_match_owner_layout()
    {
        Assert.Equal(["Name", "PID", "Status", "User name", "CPU", "Memory (active private working set)", "Command line", "UAC virtualization", "Threads", "Handles", "Commit size"], DetailsColumns.All.Select(c => c.Title));
        Assert.Equal([312, 49, 100, 80, 40, 108, 1033, 100, 34, 34, 80], DetailsColumns.All.Select(c => c.DefaultWidth));
        Assert.Equal(8, DetailsColumns.All.Count(c => c.DefaultVisible));
        Assert.False(DetailsColumns.Get(DetailsColumn.Threads).DefaultVisible);
    }

    [Fact]
    public void Columns_are_in_enum_order_with_the_first_eight_visible()
    {
        Assert.Equal(Enum.GetValues<DetailsColumn>(), DetailsColumns.All.Select(c => c.Id));
        Assert.All(Enum.GetValues<DetailsColumn>(), id => Assert.Equal(id, DetailsColumns.Get(id).Id));
        Assert.Equal(Enumerable.Range(0, 11).Select(i => i < 8), DetailsColumns.All.Select(c => c.DefaultVisible));
    }

    [Fact]
    public void Numeric_columns_are_right_aligned_and_the_rest_left()
    {
        DetailsColumn[] right = [DetailsColumn.Pid, DetailsColumn.Cpu, DetailsColumn.ActivePrivateWorkingSet, DetailsColumn.Threads, DetailsColumn.Handles, DetailsColumn.CommitSize];
        foreach (var c in DetailsColumns.All)
            Assert.Equal(right.Contains(c.Id) ? HorizontalAlignment.Right : HorizontalAlignment.Left, c.Align);
    }

    [Fact]
    public void Text_comes_from_the_documented_row_fields()
    {
        var info = new ProcessStaticInfo(@"C:\x\app.exe", "app.exe --go", @"PC\me", UacVirtualization.Enabled);
        var row = new ProcessRow(new ProcessIdentity(12345, 1), "app.exe", 4, info)
        {
            Status = ProcessStatus.Suspended, CpuPercent = 7.4, PrivateWorkingSet = 12_641_280, CommitSize = 2048, Threads = 1234, Handles = 5,
        };
        string[] expected = ["app.exe", "12345", "Suspended", @"PC\me", "07", "12,345 K", "app.exe --go", "Enabled", "1,234", "5", "2 K"];
        Assert.Equal(expected, DetailsColumns.All.Select(c => c.Text(row)));
    }

    [Fact]
    public void Missing_user_and_command_line_show_as_empty_text()
    {
        var row = R(1, "a");
        Assert.Equal("", DetailsColumns.Get(DetailsColumn.UserName).Text(row));
        Assert.Equal("", DetailsColumns.Get(DetailsColumn.CommandLine).Text(row));
        Assert.Equal("", DetailsColumns.Get(DetailsColumn.UacVirtualization).Text(row));
    }

    [Fact]
    public void Compare_uses_the_underlying_value_not_the_text()
    {
        Assert.True(DetailsColumns.Get(DetailsColumn.Pid).Compare(R(9, "a"), R(10, "a")) < 0);
        Assert.True(DetailsColumns.Get(DetailsColumn.Cpu).Compare(R(1, "a", cpu: 9), R(2, "a", cpu: 10)) < 0);
        var small = new ProcessRow(new ProcessIdentity(1, 1), "a", 0, ProcessStaticInfo.Blank) { PrivateWorkingSet = 9 * 1024, Threads = 9, Handles = 9, CommitSize = 9 * 1024 };
        var big = new ProcessRow(new ProcessIdentity(2, 1), "a", 0, ProcessStaticInfo.Blank) { PrivateWorkingSet = 10 * 1024, Threads = 10, Handles = 10, CommitSize = 10 * 1024 };
        foreach (var id in new[] { DetailsColumn.ActivePrivateWorkingSet, DetailsColumn.Threads, DetailsColumn.Handles, DetailsColumn.CommitSize })
            Assert.True(DetailsColumns.Get(id).Compare(small, big) < 0, id.ToString());
    }

    [Fact]
    public void Text_compare_ignores_case_and_treats_missing_as_empty()
    {
        Assert.True(DetailsColumns.Get(DetailsColumn.Name).Compare(R(1, "a"), R(2, "B")) < 0);
        Assert.Equal(0, DetailsColumns.Get(DetailsColumn.Name).Compare(R(1, "ABC"), R(2, "abc")));
        Assert.Equal(0, DetailsColumns.Get(DetailsColumn.UserName).Compare(R(1, "a"), R(2, "a")));
        Assert.Equal(0, DetailsColumns.Get(DetailsColumn.CommandLine).Compare(R(1, "a"), R(2, "a")));
    }

    [Fact]
    public void Text_compares_do_not_allocate()
    {
        var a = new ProcessRow(new ProcessIdentity(1, 1), "Alpha", 0, new ProcessStaticInfo(null, "app /a", @"PC\a", UacVirtualization.Unknown));
        var b = new ProcessRow(new ProcessIdentity(2, 1), "alpha2", 0, new ProcessStaticInfo(null, "app /b", @"PC\b", UacVirtualization.Unknown));
        foreach (var id in new[] { DetailsColumn.Name, DetailsColumn.UserName, DetailsColumn.CommandLine })
        {
            var compare = DetailsColumns.Get(id).Compare;
            for (var i = 0; i < 100; i++) compare(a, b);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) compare(a, b);
            Assert.True(GC.GetAllocatedBytesForCurrentThread() == before, id.ToString());
        }
    }

    [Fact]
    public void Sort_allocates_a_constant_amount_regardless_of_row_count()
    {
        var rows = Enumerable.Range(1, 500).Select(i => R(i, "p" + i % 50)).ToList();
        RowSorter.Sort(rows, DetailsColumn.Name, descending: false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        RowSorter.Sort(rows, DetailsColumn.Name, descending: true);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 1024);
    }

    [Theory]
    [InlineData(12_641_280, "12,345 K")]
    [InlineData(0, "0 K")]
    [InlineData(1023, "0 K")]
    public void Kilobytes_format(long b, string s) => Assert.Equal(s, ValueFormat.Kilobytes(b));

    [Theory]
    [InlineData(0.4, "00")]
    [InlineData(0.5, "01")]
    [InlineData(9.6, "10")]
    [InlineData(100, "100")]
    public void Cpu_format(double p, string s) => Assert.Equal(s, ValueFormat.Cpu(p));

    [Fact]
    public void Count_uses_group_separators() => Assert.Equal("1,234,567", ValueFormat.Count(1_234_567));

    [Fact]
    public void Status_and_uac_strings()
    {
        Assert.Equal("Not responding", ValueFormat.Status(ProcessStatus.NotResponding));
        Assert.Equal("Running", ValueFormat.Status(ProcessStatus.Running));
        Assert.Equal("Suspended", ValueFormat.Status(ProcessStatus.Suspended));
        Assert.Equal("Not allowed", ValueFormat.Uac(UacVirtualization.NotAllowed));
        Assert.Equal("", ValueFormat.Uac(UacVirtualization.Unknown));
        Assert.Equal("Disabled", ValueFormat.Uac(UacVirtualization.Disabled));
        Assert.Equal("Enabled", ValueFormat.Uac(UacVirtualization.Enabled));
    }

    [Fact]
    public void Sort_by_cpu_descending_breaks_ties_by_name_then_pid()
    {
        var rows = new List<ProcessRow> { R(3, "b", cpu: 5), R(2, "a", cpu: 5), R(1, "a", cpu: 5), R(4, "z", cpu: 9) };
        RowSorter.Sort(rows, DetailsColumn.Cpu, descending: true);
        Assert.Equal([4, 1, 2, 3], rows.Select(r => r.Id.Pid));
    }

    [Fact]
    public void Sort_by_cpu_ascending_keeps_the_same_tie_breakers()
    {
        var rows = new List<ProcessRow> { R(3, "b", cpu: 5), R(2, "a", cpu: 5), R(1, "a", cpu: 5), R(4, "z", cpu: 9) };
        RowSorter.Sort(rows, DetailsColumn.Cpu, descending: false);
        Assert.Equal([1, 2, 3, 4], rows.Select(r => r.Id.Pid));
    }

    [Fact]
    public void Sort_by_name_descending_still_orders_equal_names_by_pid_ascending()
    {
        var rows = new List<ProcessRow> { R(5, "A"), R(2, "a"), R(9, "c"), R(1, "a") };
        RowSorter.Sort(rows, DetailsColumn.Name, descending: true);
        Assert.Equal([9, 1, 2, 5], rows.Select(r => r.Id.Pid));
    }

    [Fact]
    public void Sort_is_deterministic_for_any_input_order()
    {
        var rows = Enumerable.Range(1, 60).Select(i => R(i, "p" + i % 4, cpu: i % 3)).ToList();
        RowSorter.Sort(rows, DetailsColumn.Cpu, descending: false);
        var expected = rows.Select(r => r.Id.Pid).ToList();
        var shuffled = new Random(7);
        for (var round = 0; round < 5; round++)
        {
            var again = rows.OrderBy(_ => shuffled.Next()).ToList();
            RowSorter.Sort(again, DetailsColumn.Cpu, descending: false);
            Assert.Equal(expected, again.Select(r => r.Id.Pid));
        }
    }

    [Fact]
    public void FindByPrefix_is_case_insensitive_and_wraps()
    {
        var rows = new[] { R(1, "chrome.exe"), R(2, "Code.exe"), R(3, "cmd.exe") };
        Assert.Equal(1, RowSearch.FindByPrefix(rows, "co", 0));
        Assert.Equal(0, RowSearch.FindByPrefix(rows, "chr", 2));
        Assert.Equal(-1, RowSearch.FindByPrefix(rows, "zz", 0));
    }

    [Fact]
    public void FindByPrefix_starts_at_the_given_index_inclusive()
    {
        var rows = new[] { R(1, "chrome.exe"), R(2, "Code.exe"), R(3, "cmd.exe"), R(4, "csrss.exe") };
        Assert.Equal(1, RowSearch.FindByPrefix(rows, "CO", 1));
        Assert.Equal(3, RowSearch.FindByPrefix(rows, "c", 3));
        Assert.Equal(0, RowSearch.FindByPrefix(rows, "c", 4));
    }

    [Fact]
    public void FindByPrefix_returns_minus_one_for_no_rows_or_no_prefix()
    {
        Assert.Equal(-1, RowSearch.FindByPrefix([], "a", 0));
        Assert.Equal(-1, RowSearch.FindByPrefix([R(1, "a")], "", 0));
    }

    [Fact]
    public void FindByPrefix_starts_at_the_first_row_when_startIndex_is_negative_or_past_the_end()
    {
        var rows = new[] { R(1, "chrome.exe"), R(2, "Code.exe"), R(3, "cmd.exe") };
        Assert.Equal(0, RowSearch.FindByPrefix(rows, "ch", -1));
        Assert.Equal(0, RowSearch.FindByPrefix(rows, "c", int.MinValue));
        Assert.Equal(2, RowSearch.FindByPrefix(rows, "cm", 7));
        Assert.Equal(0, RowSearch.FindByPrefix(rows, "c", 7));
        Assert.Equal(0, RowSearch.FindByPrefix(rows, "c", 3));
    }

    [Fact]
    public void Negative_startIndex_does_not_wrap_from_the_end()
    {
        var rows = Enumerable.Range(0, 500).Select(i => R(i, i == 0 || i == 295 ? "match" : "other")).ToList();
        Assert.Equal(0, RowSearch.FindByPrefix(rows, "match", -1));
    }

    [Fact]
    public void Restore_follows_identity_after_resort()
    {
        var rows = new List<ProcessRow> { R(1, "a"), R(2, "b"), R(3, "c") };
        var sel = new HashSet<ProcessIdentity>();
        SelectionMap.Capture(rows, [1], sel);
        rows.Reverse();
        Assert.Equal([1], SelectionMap.Restore(rows, sel));
        rows.Insert(0, R(9, "x"));
        Assert.Equal([2], SelectionMap.Restore(rows, sel));
    }

    [Fact]
    public void Exited_selection_is_dropped()
    {
        var rows = new List<ProcessRow> { R(1, "a"), R(2, "b") };
        var sel = new HashSet<ProcessIdentity>();
        SelectionMap.Capture(rows, [1], sel);
        rows.RemoveAt(1);
        Assert.Empty(SelectionMap.Restore(rows, sel));
    }

    [Fact]
    public void Restore_returns_ascending_indices_for_a_multi_selection()
    {
        var rows = new List<ProcessRow> { R(1, "a"), R(2, "b"), R(3, "c"), R(4, "d") };
        var sel = new HashSet<ProcessIdentity>();
        SelectionMap.Capture(rows, [3, 0, 2], sel);
        rows.Reverse();
        Assert.Equal([0, 1, 3], SelectionMap.Restore(rows, sel));
    }

    [Fact]
    public void Capture_replaces_the_previous_contents()
    {
        var rows = new List<ProcessRow> { R(1, "a"), R(2, "b") };
        var sel = new HashSet<ProcessIdentity>();
        SelectionMap.Capture(rows, [0], sel);
        SelectionMap.Capture(rows, [1], sel);
        Assert.Equal([new ProcessIdentity(2, 1)], sel);
        SelectionMap.Capture(rows, [], sel);
        Assert.Empty(sel);
    }

    [Fact]
    public void Capture_ignores_indices_outside_the_rows()
    {
        var rows = new List<ProcessRow> { R(1, "a") };
        var sel = new HashSet<ProcessIdentity>();
        SelectionMap.Capture(rows, [-1, 0, 1, 7], sel);
        Assert.Equal([new ProcessIdentity(1, 1)], sel);
    }

    [Fact]
    public void A_reused_pid_with_a_new_create_time_is_not_the_selected_process()
    {
        var rows = new List<ProcessRow> { R(1, "a") };
        var sel = new HashSet<ProcessIdentity>();
        SelectionMap.Capture(rows, [0], sel);
        var reborn = new List<ProcessRow> { new(new ProcessIdentity(1, 2), "a", 0, ProcessStaticInfo.Blank) };
        Assert.Empty(SelectionMap.Restore(reborn, sel));
    }
}
