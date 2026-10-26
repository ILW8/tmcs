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
}
