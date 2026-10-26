using System.Diagnostics;
using Tmcs.Actions;
using Tmcs.Processes;

namespace Tmcs.Tests.Actions;

/// <summary>
/// The real <see cref="ProcessActions"/> against processes these tests start themselves: hidden <c>cmd /c pause</c> children that wait on
/// a redirected stdin, and one hidden <c>ping</c>. Nothing else is ever ended. Every child is killed and disposed in <c>finally</c>.
/// </summary>
public class ProcessActionsTests
{
    static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    static Process Start(string arguments = "/c pause") =>
        Process.Start(new ProcessStartInfo(Cmd, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true })!;

    /// <summary>
    /// A <c>ping</c> that holds its stdin pipe but never reads it, and lives about 30 seconds unless killed. The handle closer's test
    /// target: a process blocked in a synchronous read of a pipe would block the closer instead, because the last close of that
    /// pipe waits for the read to finish.
    /// </summary>
    static Process StartHoldingIdlePipe() =>
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n 30 127.0.0.1")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true })!;

    static List<RawProcess> Snapshot()
    {
        var l = new List<RawProcess>();
        new SystemProcessReader().Read(l);
        return l;
    }

    static ProcessIdentity IdOf(Process p)
    {
        var r = Snapshot().Single(x => x.Pid == p.Id);
        return new(r.Pid, r.CreateTime);
    }

    static void Kill(Process? p)
    {
        if (p is null) return;
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
            p.WaitForExit(5000);
        }
        catch (Exception)
        {
            // Already gone.
        }
        p.Dispose();
    }

    static int HandleCount(Process p)
    {
        p.Refresh();
        return p.HandleCount;
    }

    [Fact]
    public void Refuses_system_pids()
    {
        var actions = new ProcessActions();
        foreach (var pid in new[] { 0, 1, 4, 5 })
        {
            var outcome = actions.EndTask(new(pid, 0));
            Assert.True(outcome.IsInvalidArg, $"PID {pid}: 0x{outcome.HResult:X8}");
            Assert.True(outcome.ShowDialog);
            Assert.False(outcome.IsGone);
        }
    }

    [Fact]
    public void Ends_a_child_process()
    {
        var child = Start();
        try
        {
            Assert.True(new ProcessActions().EndTask(IdOf(child)).Ok);
            Assert.True(child.WaitForExit(5000));
            Assert.Equal(1, child.ExitCode);
        }
        finally { Kill(child); }
    }

    [Fact]
    public void End_task_refuses_a_reused_pid()
    {
        var child = Start();
        try
        {
            var id = IdOf(child);
            var outcome = new ProcessActions().EndTask(id with { CreateTime = id.CreateTime + 1 });
            Assert.True(outcome.IsInvalidArg);
            Assert.True(outcome.ShowDialog);
            Assert.True(outcome.IsGone);
            Assert.False(child.WaitForExit(300));
        }
        finally { Kill(child); }
    }

    [Fact]
    public void End_task_on_a_gone_process_never_reports_ok()
    {
        // The gone child's Process object stays undisposed until the end, so its handle keeps the PID from being reused mid-test.
        var gone = Start();
        var bystander = Start();
        try
        {
            var actions = new ProcessActions();
            var id = IdOf(gone);
            Assert.True(actions.EndTask(id).Ok);
            Assert.True(gone.WaitForExit(5000));

            // With the PID still held, the open succeeds and TerminateProcess is denied: no dialog, and the handle closer is queued,
            // which finds the process already gone (STATUS_PROCESS_IS_TERMINATING) and stops.
            var again = actions.EndTask(id);
            Assert.True(again.IsAccessDenied, $"0x{again.HResult:X8}");
            Assert.False(again.ShowDialog);
            Assert.False(bystander.WaitForExit(300));
        }
        finally
        {
            Kill(bystander);
            Kill(gone);
        }
    }

    [Fact]
    public void End_process_tree_ends_each_identity()
    {
        // The parent starts two cmd children on its own console; both wait on the inherited stdin.
        var parent = Start("/c start /b cmd /c pause & start /b cmd /c pause & pause");
        var children = new List<Process>();
        try
        {
            var parentId = IdOf(parent);
            List<RawProcess> snapshot = [];
            for (var tries = 0; tries < 100; tries++)
            {
                snapshot = Snapshot();
                if (snapshot.Count(r => r.ParentPid == parentId.Pid && r.Name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)) == 2) break;
                Thread.Sleep(50);
            }
            var childRows = snapshot.Where(r => r.ParentPid == parentId.Pid && r.CreateTime > parentId.CreateTime
                                                && r.Name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(2, childRows.Count);
            children.AddRange(childRows.Select(r => Process.GetProcessById(r.Pid)));

            var rows = snapshot.Select(r => new ProcessRow(new(r.Pid, r.CreateTime), r.Name, r.ParentPid, ProcessStaticInfo.Blank)).ToList();
            var outcome = new ProcessActions().EndProcessTree(parentId, rows);

            Assert.True(outcome.Ok, $"0x{outcome.HResult:X8}");
            Assert.True(parent.WaitForExit(5000));
            Assert.All(children, c => Assert.True(c.WaitForExit(5000)));
        }
        finally
        {
            foreach (var c in children) Kill(c);
            Kill(parent);
        }
    }

    [Fact]
    public void Critical_process_is_detected()
    {
        var fake = new FakeProcessActions { CriticalPids = { 1234 } };
        Assert.True(fake.IsCritical(new(1234, 1)));
        Assert.False(fake.IsCritical(new(5678, 1)));

        // The real query on an ordinary process the test started: readable, not critical, no throw.
        var child = Start();
        try { Assert.False(new ProcessActions().IsCritical(IdOf(child))); }
        finally { Kill(child); }
    }

    [Fact]
    public void Set_priority_changes_the_class()
    {
        var child = Start();
        try
        {
            var set = new ProcessActions().SetPriority(IdOf(child), ProcessPriority.BelowNormal);

            Assert.True(set.Outcome.Ok, $"0x{set.Outcome.HResult:X8}");
            Assert.True(set.ReadBackMatched);
            child.Refresh();
            Assert.Equal(ProcessPriorityClass.BelowNormal, child.PriorityClass); // BELOW_NORMAL_PRIORITY_CLASS
        }
        finally { Kill(child); }
    }

    [Fact]
    public void Get_and_set_affinity_round_trip()
    {
        var child = Start();
        try
        {
            var actions = new ProcessActions();
            var id = IdOf(child);

            var before = actions.GetAffinity(id);
            Assert.True(before.Outcome.Ok, $"0x{before.Outcome.HResult:X8}");
            Assert.NotEqual((nuint)0, before.Info.SystemMask);
            Assert.NotEqual((nuint)0, before.Info.ProcessMask & before.Info.SystemMask);

            // The lowest CPU the process may use, on its own.
            var single = before.Info.ProcessMask & (nuint)(-(nint)before.Info.ProcessMask);
            var set = actions.SetAffinity(id, single);

            Assert.True(set.Ok, $"0x{set.HResult:X8}");
            var after = actions.GetAffinity(id);
            Assert.True(after.Outcome.Ok);
            Assert.Equal(single, after.Info.ProcessMask);
        }
        finally { Kill(child); }
    }

    [Fact]
    public void Affinity_refuses_a_reused_pid_and_the_kernel_pids()
    {
        var child = Start();
        try
        {
            var actions = new ProcessActions();
            var id = IdOf(child);
            var reused = id with { CreateTime = id.CreateTime + 1 };
            var before = actions.GetAffinity(id);

            Assert.True(actions.GetAffinity(reused).Outcome.IsGone);
            Assert.True(actions.SetAffinity(reused, 1).IsGone);
            Assert.Equal(before, actions.GetAffinity(id));

            foreach (var pid in new[] { 0, 1, 4, 5 })
            {
                var refused = actions.SetAffinity(new ProcessIdentity(pid, 1), 1);
                Assert.True(refused.IsInvalidArg);
                Assert.True(refused.ShowDialog);
                var read = actions.GetAffinity(new ProcessIdentity(pid, 1)).Outcome;
                Assert.True(read.IsInvalidArg);
                Assert.True(read.ShowDialog);
                Assert.False(read.IsGone);
            }
        }
        finally { Kill(child); }
    }

    // Ruling R-S: the refused PIDs get the "not valid for this process" dialog, never the silent gone outcome. The refusal returns
    // before OpenProcess, so no real process is touched.
    [Fact]
    public void Set_priority_refuses_a_kernel_pid_with_the_dialog()
    {
        var outcome = new ProcessActions().SetPriority(new(4, 0), ProcessPriority.Low).Outcome;

        Assert.True(outcome.IsInvalidArg);
        Assert.True(outcome.ShowDialog);
        Assert.False(outcome.IsGone);
    }

    // The refusal comes before any descendant is ended: the rows hold only made-up PIDs, which nothing may be called on.
    [Fact]
    public void End_process_tree_refuses_a_kernel_pid_before_any_descendant()
    {
        var rows = new List<ProcessRow> { new(new(4, 0), "System", 0, ProcessStaticInfo.Blank), new(new(700, 100), "child", 4, ProcessStaticInfo.Blank) };
        var fake = new FakeProcessActions();

        var outcome = fake.EndProcessTree(new(4, 0), rows);

        Assert.Equal([(nameof(FakeProcessActions.EndProcessTree), new ProcessIdentity(4, 0))], fake.Calls);
        Assert.True(outcome.IsInvalidArg);
        Assert.True(outcome.ShowDialog);
        Assert.False(outcome.IsGone);
    }

    [Fact]
    public void Set_priority_refuses_a_reused_pid()
    {
        var child = Start();
        try
        {
            var id = IdOf(child);
            var before = child.PriorityClass; // inherited from the test host, whatever its class
            var set = new ProcessActions().SetPriority(id with { CreateTime = id.CreateTime + 1 }, ProcessPriority.Low);

            Assert.True(set.Outcome.IsGone);
            child.Refresh();
            Assert.Equal(before, child.PriorityClass);
        }
        finally { Kill(child); }
    }

    [Fact]
    public void Realtime_without_privilege_reads_back_as_high()
    {
        // Without SeIncreaseBasePriorityPrivilege, SetPriorityClass(REALTIME) succeeds and sets High (reference 4, probe P6). The
        // elevated tmcs holds the privilege, so there the read-back matches whenever the class really is Realtime.
        var child = Start();
        try
        {
            var actions = new ProcessActions();
            var set = actions.SetPriority(IdOf(child), ProcessPriority.Realtime);

            Assert.True(set.Outcome.Ok, $"0x{set.Outcome.HResult:X8}");
            child.Refresh();
            if (!actions.IsElevated)
            {
                Assert.False(set.ReadBackMatched);
                Assert.Equal(ProcessPriorityClass.High, child.PriorityClass);
            }
            else
            {
                Assert.Equal(child.PriorityClass == ProcessPriorityClass.RealTime, set.ReadBackMatched);
            }
        }
        finally { Kill(child); }
    }

    [Fact]
    public void Priority_maps_to_the_class_and_from_the_base_priority()
    {
        // *_PRIORITY_CLASS values and base priorities, written out (reference 4).
        Assert.Equal(
            [0x40u, 0x4000u, 0x20u, 0x8000u, 0x80u, 0x100u],
            Enum.GetValues<ProcessPriority>().Select(ProcessPriorities.ToClass));
        Assert.Equal(
            [ProcessPriority.Low, ProcessPriority.BelowNormal, ProcessPriority.Normal, ProcessPriority.AboveNormal, ProcessPriority.High, ProcessPriority.Realtime],
            new[] { 4, 6, 8, 10, 13, 24 }.Select(b => ProcessPriorities.FromBasePriority(b)!.Value));
        Assert.All(new[] { 0, 5, 7, 9, 11, 15, 31 }, b => Assert.Null(ProcessPriorities.FromBasePriority(b)));
    }

    [Fact]
    public async Task Handle_closer_touches_only_the_target()
    {
        var target = StartHoldingIdlePipe();
        var bystander = Start();
        try
        {
            await Task.Delay(1000);    // let ping finish starting and send its first echo
            var id = IdOf(target);
            var targetBefore = HandleCount(target);
            var bystanderBefore = HandleCount(bystander);
            using var self = Process.GetCurrentProcess();
            var selfBefore = HandleCount(self);

            // Run on a worker so a hang fails the test instead of stalling the run.
            var run = Task.Run(() => ProcessActions.CloseHandlesOf(id));
            Assert.True(await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30))) == run, "the closer did not return");

            // ping survives losing its handles, so the drop can be seen: about 110 handles down to about 12 (the console, and the
            // File handles the filter keeps).
            Assert.False(target.HasExited);
            Assert.True(HandleCount(target) < targetBefore / 2, $"target handles {targetBefore} -> {HandleCount(target)}");
            Assert.Equal(bystanderBefore, HandleCount(bystander));
            Assert.InRange(HandleCount(self), 0, selfBefore + 10);
        }
        finally
        {
            Kill(bystander);
            Kill(target);
        }
    }
}
