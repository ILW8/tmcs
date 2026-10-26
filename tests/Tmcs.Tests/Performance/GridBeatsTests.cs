using Tmcs.Performance;

namespace Tmcs.Tests.Performance;

public class GridBeatsTests
{
    const long Second = 10_000_000;

    [Fact] public void The_first_sample_is_a_beat_even_from_a_refresh()
    { var b = new GridBeats(); b.Note(5 * Second, timed: false, Second);
      Assert.Equal(1, b.Count); Assert.Equal(5 * Second, b.TimeAt(0)); Assert.Equal(0, b.NumberAt(0)); }

    [Fact] public void Every_timer_sample_is_a_beat()
    { var b = new GridBeats(); b.Note(Second, timed: false, Second);
      b.Note(Second + 1000, timed: true, Second); // a tick just after the startup refresh still counts
      b.Note(2 * Second, timed: true, Second);
      Assert.Equal(3, b.Count); Assert.Equal(2, b.NumberAt(2)); }

    [Fact] public void A_refresh_counts_only_an_interval_or_more_after_the_last_beat()
    { var b = new GridBeats(); b.Note(Second, timed: true, Second);
      for (var k = 1; k <= 30; k++) b.Note(Second + k * 300_000, timed: false, Second); // F5 every 30 ms for 0.9 s
      Assert.Equal(1, b.Count);
      b.Note(2 * Second, timed: false, Second); // paused: one interval later, a refresh is a beat
      Assert.Equal(2, b.Count); Assert.Equal(2 * Second, b.TimeAt(1)); }

    [Fact] public void A_time_not_after_the_last_beat_is_ignored()
    { var b = new GridBeats(); b.Note(10, timed: true, Second); b.Note(10, timed: true, Second); b.Note(5, timed: true, Second);
      Assert.Equal(1, b.Count); }

    [Fact] public void The_ring_keeps_the_newest_beats_and_their_numbers()
    { var b = new GridBeats();
      for (var i = 1; i <= GridBeats.Capacity + 10; i++) b.Note(i * Second, timed: true, Second);
      Assert.Equal(GridBeats.Capacity, b.Count); Assert.Equal(11 * Second, b.TimeAt(0));
      Assert.Equal(10, b.NumberAt(0)); Assert.Equal(GridBeats.Capacity + 9, b.NumberAt(b.Count - 1)); }

    [Fact] public void Note_does_not_allocate()
    { var b = new GridBeats(); b.Note(1, timed: true, Second);
      var before = GC.GetAllocatedBytesForCurrentThread();
      for (var i = 0; i < 1000; i++) b.Note(2 + i * Second, timed: i % 2 == 0, Second);
      Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread()); }
}
