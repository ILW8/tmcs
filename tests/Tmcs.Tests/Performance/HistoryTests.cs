using Tmcs.Performance;

namespace Tmcs.Tests.Performance;

public class HistoryTests
{
    const long Second = 10_000_000;

    static History Filled(int points, long step100Ns, int series = 1)
    {
        var h = new History(series);
        Span<float> v = stackalloc float[series];
        for (var i = 1; i <= points; i++) { v.Fill(i); h.Add(i * step100Ns, v); }
        return h;
    }

    [Fact] public void Add_keeps_order_and_caps_at_capacity()
    { var h = Filled(History.Capacity + 9, 5_000_000);
      Assert.Equal(History.Capacity, h.Count); Assert.Equal(10 * 5_000_000L, h.TimeAt(0));
      Assert.Equal((History.Capacity + 9) * 5_000_000L, h.LatestTime); Assert.Equal(History.Capacity + 9f, h.ValueAt(0, h.Count - 1)); }

    [Fact] public void First_visible_is_the_newest_point_at_or_before_59_s_ago()
    { var h = Filled(100, Second); // one point a second, 100 s
      Assert.Equal(41 * Second, h.TimeAt(h.FirstVisible));
      Assert.Equal(60, h.Count - h.FirstVisible); } // Task Manager's 60 samples

    // A refresh 0.1 s after a tick folds into its point, as the mean over both spans weighted by their lengths.
    [Fact] public void A_sample_closer_than_the_minimum_spacing_folds_into_the_newest_point()
    { var h = new History(1); h.Add(Second, [10f]); h.Add(2 * Second, [20f]); h.Add(2 * Second + Second / 10, [40f]);
      Assert.Equal(2, h.Count); Assert.Equal(2 * Second + Second / 10, h.LatestTime);
      Assert.Equal((20.0 * 1.0 + 40.0 * 0.1) / 1.1, h.ValueAt(0, 1), 4); }

    // F5 held for 70 s, a press every 30 ms, after 121 s at Normal speed, then 20 s of ticks. The ring still covers the
    // window, and no stretch of the graph turns into one straight line.
    [Fact] public void A_held_refresh_keeps_the_graph_whole()
    { var h = Filled(121, Second); var t = 121 * Second;
      for (var k = 0; k < 70_000 / 30; k++) h.Add(t += 300_000, [k % 2 * 100f]);
      var held = t;
      for (var k = 0; k < 20; k++) h.Add(t += Second, [50f]);
      Assert.True(h.Count <= History.Capacity);
      Assert.True(h.TimeAt(h.FirstVisible) <= h.LatestTime - History.Window100Ns, "the line no longer reaches the left border");
      for (var i = h.FirstVisible + 1; i < h.Count; i++)
      { var gap = h.TimeAt(i) - h.TimeAt(i - 1);
        Assert.True(gap <= (h.TimeAt(i) <= held ? Second / 2 : Second), $"a {gap / 1e7:0.00}-s straight line ends at {h.TimeAt(i) / 1e7:0.00} s"); } }

    [Fact] public void Ticks_at_high_speed_never_fold()
    { var h = new History(1); var t = 0L;
      for (var i = 0; i < 100; i++) h.Add(t += i % 2 == 0 ? 4_500_000 : 5_500_000, [i]); // 0.45 s and 0.55 s apart
      Assert.Equal(100, h.Count); }

    [Fact] public void First_visible_is_0_when_history_is_shorter_than_the_window()
    { Assert.Equal(0, Filled(10, Second).FirstVisible); }

    [Fact] public void Series_are_kept_apart()
    { var h = new History(2); h.Add(Second, [1f, 10f]); h.Add(2 * Second, [2f, 20f]);
      Assert.Equal(20f, h.ValueAt(1, 1)); Assert.Equal(1f, h.ValueAt(0, 0)); }

    [Fact] public void Max_visible_ignores_points_older_than_the_window()
    { var h = new History(1); h.Add(1, [99f]);
      for (var s = 1; s <= 70; s++) h.Add(1 + s * Second, [s]);
      Assert.Equal(70f, h.MaxVisible(0)); Assert.True(h.TimeAt(h.FirstVisible) > 1); }

    [Fact] public void Time_not_after_latest_is_ignored()
    { var h = new History(1); h.Add(10, [1f]); h.Add(10, [2f]); h.Add(5, [3f]);
      Assert.Equal(1, h.Count); Assert.Equal(1f, h.ValueAt(0, 0)); }

    [Fact] public void Wrong_value_count_throws()
    { Assert.Throws<ArgumentException>(() => new History(2).Add(1, [1f])); }

    [Fact] public void Add_does_not_allocate()
    { var h = Filled(200, 5_000_000, series: 3); Span<float> v = stackalloc float[3];
      var before = GC.GetAllocatedBytesForCurrentThread();
      for (var i = 0; i < 1000; i++) h.Add(2_000_000_000L + i * 1_000_000, v); // a fold every 0.1 s, an append every 0.4 s
      Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread()); }
}
