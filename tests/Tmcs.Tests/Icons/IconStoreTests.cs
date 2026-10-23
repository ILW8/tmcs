using Tmcs.Icons;

namespace Tmcs.Tests.Icons;

public class IconStoreTests
{
    private sealed class FakeSink : IImageListSink
    {
        public int Count { get; private set; }
        public List<int> Replaced { get; } = [];
        public bool FailAdds { get; set; }

        public int Add(nint hIcon) => FailAdds ? -1 : Count++;

        public void Replace(int index, nint hIcon) => Replaced.Add(index);
    }

    private sealed class FakeLoader : IIconLoader
    {
        private readonly HashSet<nint> _outstanding = [];
        private nint _next = 100;

        public int Calls { get; private set; }
        public int Loads { get; private set; }
        public int Destroys { get; private set; }
        public int Outstanding => _outstanding.Count;

        public nint LoadSmallIcon(string path)
        {
            Calls++;
            if (path.StartsWith("bad:", StringComparison.Ordinal)) return 0;
            Loads++;
            _outstanding.Add(++_next);
            return _next;
        }

        public void DestroyIcon(nint hIcon)
        {
            Assert.True(_outstanding.Remove(hIcon), "an icon was destroyed twice or was never loaded");
            Destroys++;
        }
    }

    private static (IconStore Store, FakeSink Sink, FakeLoader Loader) Make(int capacity = 64)
    {
        var sink = new FakeSink();
        var loader = new FakeLoader();
        return (new IconStore(sink, loader, genericIcon: 1, capacity), sink, loader);
    }

    [Fact] public void Same_path_shares_one_slot()
    { var (s, _, ld) = Make(); var slots = Enumerable.Range(0, 50).Select(_ => s.Acquire(@"C:\w\cmd.exe")).Distinct().ToList();
      Assert.Single(slots); Assert.Equal(2, s.SlotCount); Assert.Equal(1, s.LiveCount); Assert.Equal(1, ld.Loads); }

    [Fact] public void Path_compare_is_case_insensitive()
    { var (s, _, ld) = Make(); Assert.Equal(s.Acquire(@"C:\W\CMD.EXE"), s.Acquire(@"c:\w\cmd.exe")); Assert.Equal(1, ld.Loads); }

    [Fact] public void Released_path_is_pooled_and_reacquired_without_reload()
    { var (s, _, ld) = Make(); var a = s.Acquire("p"); s.Release("p"); Assert.Equal(0, s.LiveCount); Assert.Equal(1, s.PooledCount);
      Assert.Equal(a, s.Acquire("p")); Assert.Equal(1, ld.Loads); }

    [Fact] public void Churn_of_distinct_paths_stays_bounded_and_destroys_every_icon()
    { var (s, _, ld) = Make(capacity: 4);
      for (int i = 0; i < 1000; i++) { s.Acquire("p" + i); s.Release("p" + i); }
      Assert.InRange(s.SlotCount, 1, 1 + 4 + 1); Assert.Equal(ld.Loads, ld.Destroys); Assert.Equal(4, s.PooledCount); }

    [Fact] public void Load_failure_returns_generic_and_consumes_no_slot()
    { var (s, _, _) = Make(); Assert.Equal(IconStore.GenericSlot, s.Acquire("bad:x")); Assert.Equal(1, s.SlotCount); }

    [Fact] public void Null_path_returns_generic() { var (s, _, _) = Make(); Assert.Equal(IconStore.GenericSlot, s.Acquire(null)); s.Release(null); }

    [Fact]
    public void Empty_path_returns_generic_without_loading()
    {
        var (s, _, ld) = Make();
        Assert.Equal(IconStore.GenericSlot, s.Acquire(""));
        s.Release("");
        Assert.Equal(0, ld.Calls);
        Assert.Equal(0, s.LiveCount);
    }

    [Fact]
    public void Constructor_adds_the_generic_icon_at_slot_zero_and_leaves_its_handle_to_the_caller()
    {
        var (s, sink, ld) = Make();
        Assert.Equal(1, sink.Count);
        Assert.Equal(1, s.SlotCount);
        Assert.Equal(0, ld.Destroys);
    }

    [Fact]
    public void Constructor_rejects_a_sink_that_already_holds_images()
    {
        var sink = new FakeSink();
        sink.Add(7);
        Assert.Throws<InvalidOperationException>(() => new IconStore(sink, new FakeLoader(), genericIcon: 1));
    }

    [Fact]
    public void Entry_stays_live_until_its_last_holder_releases()
    {
        var (s, _, _) = Make();
        s.Acquire("p"); s.Acquire("p");
        s.Release("p");
        Assert.Equal(1, s.LiveCount); Assert.Equal(0, s.PooledCount);
        s.Release("p");
        Assert.Equal(0, s.LiveCount); Assert.Equal(1, s.PooledCount);
    }

    [Fact]
    public void Release_of_an_unknown_path_is_a_no_op()
    {
        var (s, _, _) = Make();
        s.Acquire("p");
        s.Release("never-acquired");
        Assert.Equal(1, s.LiveCount); Assert.Equal(0, s.PooledCount); Assert.Equal(2, s.SlotCount);
    }

    [Fact]
    public void Extra_release_of_a_pooled_path_is_ignored()
    {
        var (s, _, ld) = Make(capacity: 2);
        var a = s.Acquire("a");
        s.Release("a"); s.Release("a");
        Assert.Equal(0, s.LiveCount); Assert.Equal(1, s.PooledCount);
        Assert.Equal(a, s.Acquire("a")); Assert.Equal(1, s.LiveCount); Assert.Equal(0, s.PooledCount);
        Assert.Equal(1, ld.Loads);
    }

    [Fact]
    public void Eviction_drops_the_path_released_longest_ago()
    {
        var (s, _, ld) = Make(capacity: 2);
        s.Acquire("a"); s.Acquire("b"); s.Acquire("c");
        s.Release("b"); s.Release("a"); s.Release("c");   // pool is over capacity: b was released first
        Assert.Equal(2, s.PooledCount);
        s.Acquire("a"); s.Acquire("c");
        Assert.Equal(3, ld.Loads);
        s.Acquire("b");
        Assert.Equal(4, ld.Loads);
    }

    [Fact]
    public void Reacquiring_a_pooled_path_protects_it_from_eviction()
    {
        var (s, _, ld) = Make(capacity: 2);
        s.Acquire("a"); s.Release("a");
        s.Acquire("b"); s.Release("b");
        s.Acquire("a");                                    // a leaves the pool and is live again
        s.Acquire("c"); s.Release("c");
        s.Release("a");                                    // pool: a, c, b is over capacity: b goes
        s.Acquire("a"); s.Acquire("c");
        Assert.Equal(3, ld.Loads);
    }

    [Fact]
    public void Evicted_slot_is_reused_for_the_next_new_path()
    {
        var (s, sink, _) = Make(capacity: 1);
        var a = s.Acquire("a"); s.Release("a");
        s.Acquire("b"); s.Release("b");                    // evicts a, freeing its slot
        Assert.Empty(sink.Replaced);
        var c = s.Acquire("c");
        Assert.Equal(a, c);
        Assert.Equal([a], sink.Replaced);
        Assert.Equal(3, s.SlotCount);
    }

    [Fact]
    public void Failed_load_is_cached_for_the_life_of_the_entry()
    {
        var (s, _, ld) = Make();
        Assert.Equal(IconStore.GenericSlot, s.Acquire("bad:x"));
        s.Release("bad:x");
        Assert.Equal(IconStore.GenericSlot, s.Acquire("bad:x"));
        Assert.Equal(1, ld.Calls);
    }

    [Fact]
    public void Evicting_a_failed_load_never_frees_the_generic_slot()
    {
        var (s, sink, _) = Make(capacity: 1);
        s.Acquire("bad:a"); s.Release("bad:a");
        s.Acquire("bad:b"); s.Release("bad:b");            // evicts bad:a, which sits in the generic slot
        Assert.Equal(1, s.PooledCount);
        var p = s.Acquire("p");
        Assert.NotEqual(IconStore.GenericSlot, p);
        Assert.Empty(sink.Replaced);                       // the generic image was not overwritten
        Assert.Equal(2, s.SlotCount);
    }

    [Fact]
    public void Add_failure_falls_back_to_generic_and_still_destroys_the_icon()
    {
        var (s, sink, ld) = Make();
        sink.FailAdds = true;
        Assert.Equal(IconStore.GenericSlot, s.Acquire("p"));
        Assert.Equal(1, s.SlotCount);
        Assert.Equal(0, ld.Outstanding);
    }

    [Fact]
    public void Every_loaded_icon_is_destroyed_right_after_it_is_copied()
    {
        var (s, _, ld) = Make();
        for (int i = 0; i < 10; i++) s.Acquire("p" + i);   // all still live
        Assert.Equal(10, ld.Loads);
        Assert.Equal(10, ld.Destroys);
        Assert.Equal(0, ld.Outstanding);
    }
}
