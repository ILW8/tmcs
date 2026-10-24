using Tmcs.Processes;

namespace Tmcs.Tests.Processes;

public class ProcessTableTests
{
    static RawProcess P(int pid, long ct, long k = 0, long u = 0, bool susp = false) =>
        new(pid, 1, ct, "p" + pid, k, u, 100, 200, 3, 4, 1, 8, susp);

    [Fact]
    public void Apply_adds_rows_and_queries_static_info_once()
    {
        var info = new FakeInfo(); var t = new ProcessTable(info, 4); var added = 0; t.Added += _ => added++;
        t.Apply([P(10, 1), P(11, 1)], 0); t.Apply([P(10, 1), P(11, 1)], 10_000_000);
        Assert.Equal(2, t.Count); Assert.Equal(2, added); Assert.Equal(1, info.Calls[new(10, 1)]);
    }

    [Fact]
    public void Exited_process_is_removed_on_the_same_tick()
    {
        var t = new ProcessTable(new FakeInfo(), 4); ProcessRow? gone = null; t.Removed += r => gone = r;
        t.Apply([P(10, 1), P(11, 1)], 0); t.Apply([P(10, 1)], 10_000_000);
        Assert.Equal(1, t.Count); Assert.Equal(new ProcessIdentity(11, 1), gone!.Id);
    }

    [Fact]
    public void Pid_reuse_with_new_create_time_is_a_new_row()
    {
        var info = new FakeInfo(); var t = new ProcessTable(info, 4); var events = new List<string>();
        t.Removed += r => events.Add("-" + r.Id.CreateTime); t.Added += r => events.Add("+" + r.Id.CreateTime);
        t.Apply([P(10, 1)], 0); t.Apply([P(10, 2)], 10_000_000);
        Assert.Equal(["+1", "-1", "+2"], events); Assert.Equal(new ProcessIdentity(10, 2), t.Rows.Single().Id);
        Assert.Equal(1, info.Calls[new(10, 2)]);
    }

    [Fact]
    public void Cpu_percent_is_time_delta_over_wall_times_logical_processors()
    {
        var t = new ProcessTable(new FakeInfo(), 4);
        t.Apply([P(10, 1, k: 0, u: 0)], 0); t.Apply([P(10, 1, k: 2_000_000, u: 2_000_000)], 10_000_000);
        Assert.Equal(10.0, t.Rows.Single().CpuPercent, 6);
    }

    [Fact]
    public void New_process_shows_zero_cpu_on_first_tick()
    {
        var t = new ProcessTable(new FakeInfo(), 4); t.Apply([P(10, 1, k: 9_000_000)], 0); Assert.Equal(0, t.Rows.Single().CpuPercent);
    }

    [Fact]
    public void Hung_marks_not_responding_but_suspended_wins()
    {
        var t = new ProcessTable(new FakeInfo(), 4); t.Apply([P(10, 1), P(11, 1, susp: true)], 0);
        t.ApplyHung(new HashSet<int> { 10, 11 });
        Assert.Equal(ProcessStatus.NotResponding, t.Rows.Single(r => r.Id.Pid == 10).Status);
        Assert.Equal(ProcessStatus.Suspended, t.Rows.Single(r => r.Id.Pid == 11).Status);
    }

    // Beyond the brief's six: behaviors the table is required to have but the six don't pin down.

    [Fact]
    public void Cpu_wall_delta_uses_the_previous_tick_timestamp()
    {
        var t = new ProcessTable(new FakeInfo(), 1);
        t.Apply([P(10, 1, k: 0)], 0);
        t.Apply([P(10, 1, k: 1_000_000)], 10_000_000);      // 1M cpu over 10M wall = 10 %
        t.Apply([P(10, 1, k: 3_000_000)], 14_000_000);      // 2M cpu over 4M wall = 50 % (not 2M over 14M)
        Assert.Equal(50.0, t.Rows.Single().CpuPercent, 6);
    }

    [Fact]
    public void Apply_refreshes_live_fields_and_clears_not_responding()
    {
        var t = new ProcessTable(new FakeInfo(), 4);
        t.Apply([P(10, 1)], 0); t.ApplyHung(new HashSet<int> { 10 });
        var row = t.Rows.Single(); Assert.Equal(ProcessStatus.NotResponding, row.Status);
        t.Apply([new RawProcess(10, 1, 1, "p10", 5, 6, 111, 222, 33, 44, 1, 8, false)], 10_000_000);
        Assert.Same(row, t.Rows.Single());
        Assert.Equal(ProcessStatus.Running, row.Status);
        Assert.Equal((111, 222, 33, 44, 5L, 6L),
            (row.PrivateWorkingSet, row.CommitSize, row.Threads, row.Handles, row.KernelTime, row.UserTime));
    }

    [Fact]
    public void Removing_several_rows_keeps_the_remaining_rows_intact()
    {
        var t = new ProcessTable(new FakeInfo(), 4); var removed = new List<int>(); t.Removed += r => removed.Add(r.Id.Pid);
        t.Apply([P(1, 1), P(2, 1), P(3, 1), P(4, 1), P(5, 1)], 0);
        t.Apply([P(2, 1), P(5, 1)], 10_000_000);
        Assert.Equal([1, 3, 4], removed.Order());
        Assert.Equal([2, 5], t.Rows.Select(r => r.Id.Pid).Order());
        Assert.Equal(2, t.Count);
    }

    [Fact]
    public void Removed_is_raised_before_added_within_a_tick()
    {
        var t = new ProcessTable(new FakeInfo(), 4); var events = new List<string>();
        t.Removed += r => events.Add("-" + r.Id.Pid); t.Added += r => events.Add("+" + r.Id.Pid);
        t.Apply([P(1, 1)], 0); events.Clear();
        t.Apply([P(2, 1), P(3, 1)], 10_000_000);
        Assert.Equal(["-1", "+2", "+3"], events);
    }
}
