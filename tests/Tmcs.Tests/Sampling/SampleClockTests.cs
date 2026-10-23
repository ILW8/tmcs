using Tmcs.Sampling;

namespace Tmcs.Tests.Sampling;

public class SampleClockTests
{
    [Fact]
    public void Intervals_match_task_manager()
    {
        Assert.Equal(500, SampleClock.IntervalFor(UpdateSpeed.High));
        Assert.Equal(1000, SampleClock.IntervalFor(UpdateSpeed.Normal));
        Assert.Equal(4000, SampleClock.IntervalFor(UpdateSpeed.Low));
    }

    [Fact]
    public void Paused_has_no_interval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SampleClock.IntervalFor(UpdateSpeed.Paused));
    }

    [Fact]
    public void Starts_at_normal_speed_with_the_timer_running()
    {
        var timer = new FakeTimer();
        var clock = new SampleClock(timer);

        Assert.Equal(UpdateSpeed.Normal, clock.Speed);
        Assert.Equal(1000, timer.IntervalMs);
        Assert.True(timer.Enabled);
        Assert.Equal(0, clock.SamplesTaken);
    }

    [Fact]
    public void Speed_sets_timer_interval_and_enables_it()
    {
        var timer = new FakeTimer();
        var clock = new SampleClock(timer);

        clock.Speed = UpdateSpeed.Low;
        Assert.Equal(4000, timer.IntervalMs);
        Assert.True(timer.Enabled);

        clock.Speed = UpdateSpeed.High;
        Assert.Equal(500, timer.IntervalMs);
        Assert.True(timer.Enabled);
    }

    [Fact]
    public void Each_tick_raises_Sample_and_counts_it()
    {
        var timer = new FakeTimer();
        var clock = new SampleClock(timer);
        var raised = 0;
        clock.Sample += () => raised++;

        timer.Fire();
        timer.Fire();
        timer.Fire();

        Assert.Equal(3, raised);
        Assert.Equal(3, clock.SamplesTaken);
    }

    [Fact]
    public void Changing_speed_does_not_take_a_sample()
    {
        var timer = new FakeTimer();
        var clock = new SampleClock(timer);
        var raised = 0;
        clock.Sample += () => raised++;

        clock.Speed = UpdateSpeed.High;
        clock.Speed = UpdateSpeed.Paused;
        clock.Speed = UpdateSpeed.Low;

        Assert.Equal(0, raised);
        Assert.Equal(0, clock.SamplesTaken);
    }

    [Fact]
    public void Paused_disables_timer_and_takes_no_samples()
    {
        var timer = new FakeTimer();
        var clock = new SampleClock(timer);
        var raised = 0;
        clock.Sample += () => raised++;

        clock.Speed = UpdateSpeed.Paused;
        Assert.False(timer.Enabled);

        // A real timer would not fire while disabled. Firing anyway proves the clock ignores a stray tick.
        timer.Fire();

        Assert.Equal(0, clock.SamplesTaken);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Leaving_paused_enables_the_timer_again_at_the_new_interval()
    {
        var timer = new FakeTimer();
        var clock = new SampleClock(timer);
        var raised = 0;
        clock.Sample += () => raised++;
        clock.Speed = UpdateSpeed.Paused;

        clock.Speed = UpdateSpeed.High;
        Assert.True(timer.Enabled);
        Assert.Equal(500, timer.IntervalMs);

        timer.Fire();
        Assert.Equal(1, raised);
        Assert.Equal(1, clock.SamplesTaken);
    }

    [Fact]
    public void RefreshNow_samples_once_even_when_paused()
    {
        var timer = new FakeTimer();
        var clock = new SampleClock(timer);
        var raised = 0;
        clock.Sample += () => raised++;
        clock.Speed = UpdateSpeed.Paused;

        clock.RefreshNow();

        Assert.Equal(1, clock.SamplesTaken);
        Assert.Equal(1, raised);
        Assert.False(timer.Enabled);
        Assert.Equal(UpdateSpeed.Paused, clock.Speed);
    }

    [Fact]
    public void RefreshNow_while_running_adds_one_sample_and_leaves_the_timer_alone()
    {
        var timer = new FakeTimer();
        var clock = new SampleClock(timer);
        clock.Speed = UpdateSpeed.Low;
        var raised = 0;
        clock.Sample += () => raised++;

        clock.RefreshNow();

        Assert.Equal(1, raised);
        Assert.Equal(1, clock.SamplesTaken);
        Assert.True(timer.Enabled);
        Assert.Equal(4000, timer.IntervalMs);
    }

    [Fact]
    public void Invalid_speed_is_rejected_and_changes_nothing()
    {
        var timer = new FakeTimer();
        var clock = new SampleClock(timer);
        clock.Speed = UpdateSpeed.Low;

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Speed = (UpdateSpeed)99);

        Assert.Equal(UpdateSpeed.Low, clock.Speed);
        Assert.Equal(4000, timer.IntervalMs);
        Assert.True(timer.Enabled);
    }
}
