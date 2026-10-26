using Tmcs.Actions;
using Tmcs.Processes;

namespace Tmcs.Tests.Actions;

public class ProcessTreeTests
{
    static ProcessRow Row(int pid, long created, int parent) => new(new(pid, created), "p" + pid, parent, ProcessStaticInfo.Blank);

    [Fact]
    public void Orders_descendants_before_their_parent()
    {
        // 10 ─┬─ 20 ── 21
        //     └─ 30
        var root = Row(10, 100, 1);
        var rows = new[] { Row(30, 300, 10), root, Row(21, 210, 20), Row(20, 200, 10), Row(99, 50, 1) };

        var order = ProcessTree.OrderForEnding(root.Id, rows).Select(i => i.Pid).ToList();

        Assert.Equal([10, 20, 21, 30], order.Order());
        Assert.Equal(10, order[^1]);
        Assert.True(order.IndexOf(21) < order.IndexOf(20));
    }

    [Fact]
    public void Excludes_a_reused_parent_pid()
    {
        // 40 names 10 as its parent, but it started before 10 did, so its parent was an earlier process with PID 10.
        // Its own child 41 must not come in through it either.
        var root = Row(10, 100, 1);
        var rows = new[] { root, Row(40, 90, 10), Row(41, 120, 40), Row(20, 200, 10) };

        Assert.Equal([new(20, 200), new(10, 100)], ProcessTree.OrderForEnding(root.Id, rows));
    }

    [Fact]
    public void A_leaf_returns_just_itself()
    {
        var leaf = Row(21, 210, 20);
        var rows = new[] { Row(20, 200, 10), leaf, Row(30, 300, 10) };

        Assert.Equal([leaf.Id], ProcessTree.OrderForEnding(leaf.Id, rows));
    }

    [Fact]
    public void A_gone_descendant_is_skipped_but_a_failed_root_is_not()
    {
        // 10 ─┬─ 20 (gone since the snapshot)
        //     └─ 30
        var root = Row(10, 100, 1);
        var rows = new[] { root, Row(20, 200, 10), Row(30, 300, 10) };
        var denied = new ActionOutcome(ActionOutcome.EAccessDenied, ShowDialog: true);

        var ended = new List<int>();
        var result = ProcessTree.EndEach(root.Id, rows, id => { ended.Add(id.Pid); return id.Pid == 20 ? ActionOutcome.Gone : ActionOutcome.Success; });
        Assert.Equal(ActionOutcome.Success, result);
        Assert.Equal([10, 20, 30], ended.Order());
        Assert.Equal(10, ended[^1]);

        // The root keeps the normal rules: gone is still the "not valid for this process" dialog.
        Assert.Equal(ActionOutcome.Gone, ProcessTree.EndEach(root.Id, rows, id => id.Pid == 10 ? ActionOutcome.Gone : ActionOutcome.Success));

        // A descendant that fails for any other reason still counts, and the first dialog-worthy failure wins.
        Assert.Equal(denied, ProcessTree.EndEach(root.Id, rows, id => id.Pid == 10 ? ActionOutcome.Gone : id.Pid == 30 ? denied : ActionOutcome.Success));
    }
}
