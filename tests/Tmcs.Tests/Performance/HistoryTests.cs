using Tmcs.Performance;

namespace Tmcs.Tests.Performance;

public class HistoryTests
{
    static History Filled(int points, long step100Ns, int series = 1)
    {
        var h = new History(series);
        Span<float> v = stackalloc float[series];
        for (var i = 1; i <= points; i++) { v.Fill(i); h.Add(i * step100Ns, v); }
        return h;
    }

    [Fact] public void Add_keeps_order_and_caps_at_capacity()
    { var h = Filled(130, 5_000_000);
      Assert.Equal(History.Capacity, h.Count); Assert.Equal(10 * 5_000_000L, h.TimeAt(0));
      Assert.Equal(130 * 5_000_000L, h.LatestTime); Assert.Equal(130f, h.ValueAt(0, h.Count - 1));
      Assert.Equal(9, h.SequenceAt(0)); Assert.Equal(129, h.SequenceAt(h.Count - 1)); }

    [Fact] public void First_visible_is_the_newest_point_at_or_before_59_s_ago()
    { var h = Filled(100, 10_000_000); // one point a second, 100 s
      Assert.Equal(41 * 10_000_000L, h.TimeAt(h.FirstVisible));
      Assert.Equal(60, h.Count - h.FirstVisible); } // Task Manager's 60 samples

    // F5 pressed 100 times in 0.1 s after 121 s at Normal speed. The points from 1 s to 61 s can go, and the rest replace the
    // newest point, so the line still enters at the left border.
    [Fact] public void Quick_refreshes_keep_the_window_covered()
    { var h = Filled(121, 10_000_000);
      for (var k = 1; k <= 100; k++) h.Add(121 * 10_000_000L + k * 10_000, [k]);
      Assert.Equal(History.Capacity, h.Count);
      Assert.True(h.TimeAt(h.FirstVisible) <= h.LatestTime - History.Window100Ns, "the line no longer reaches the left border");
      Assert.Equal(121 * 10_000_000L + 100 * 10_000, h.LatestTime); Assert.Equal(100f, h.ValueAt(0, h.Count - 1));
      Assert.Equal(60f, h.ValueAt(0, h.Count - 2)); Assert.Equal(121f, h.ValueAt(0, h.Count - 62)); // 60 presses kept, then 121 s
      Assert.Equal(181, h.SequenceAt(h.Count - 1)); } // 121 + 61 appended: a replaced point keeps its sequence

    [Fact] public void First_visible_is_0_when_history_is_shorter_than_the_window()
    { Assert.Equal(0, Filled(10, 10_000_000).FirstVisible); }

    [Fact] public void Series_are_kept_apart()
    { var h = new History(2); h.Add(1, [1f, 10f]); h.Add(2, [2f, 20f]);
      Assert.Equal(20f, h.ValueAt(1, 1)); Assert.Equal(1f, h.ValueAt(0, 0)); }

    [Fact] public void Max_visible_ignores_points_older_than_the_window()
    { var h = new History(1); h.Add(1, [99f]);
      for (var s = 1; s <= 70; s++) h.Add(1 + s * 10_000_000L, [s]);
      Assert.Equal(70f, h.MaxVisible(0)); Assert.True(h.TimeAt(h.FirstVisible) > 1); }

    [Fact] public void Time_not_after_latest_is_ignored()
    { var h = new History(1); h.Add(10, [1f]); h.Add(10, [2f]); h.Add(5, [3f]);
      Assert.Equal(1, h.Count); Assert.Equal(1f, h.ValueAt(0, 0)); }

    [Fact] public void Wrong_value_count_throws()
    { Assert.Throws<ArgumentException>(() => new History(2).Add(1, [1f])); }

    [Fact] public void Add_does_not_allocate()
    { var h = Filled(200, 5_000_000, series: 3); Span<float> v = stackalloc float[3];
      var before = GC.GetAllocatedBytesForCurrentThread();
      for (var i = 0; i < 1000; i++) h.Add(2_000_000_000L + i, v);
      Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread()); }
}
