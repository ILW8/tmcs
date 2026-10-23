using Tmcs.Sampling;

namespace Tmcs.Tests.Sampling;

public class WinFormsSampleTimerTests
{
    private static void PumpUntil(Func<bool> done, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!done() && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(5);
            Application.DoEvents();
        }
    }

    [Fact]
    public void Interval_and_enabled_pass_through_to_the_wrapped_timer()
    {
        Sta.Run(() =>
        {
            using var timer = new WinFormsSampleTimer();
            Assert.False(timer.Enabled);

            timer.IntervalMs = 250;
            timer.Enabled = true;
            Assert.Equal(250, timer.IntervalMs);
            Assert.True(timer.Enabled);

            timer.Enabled = false;
            Assert.False(timer.Enabled);
        });
    }

    [Fact]
    public void Tick_is_raised_with_the_wrapper_as_sender_while_enabled()
    {
        Sta.Run(() =>
        {
            using var timer = new WinFormsSampleTimer { IntervalMs = 5 };
            object? sender = null;
            var ticks = 0;
            timer.Tick += (s, _) =>
            {
                sender = s;
                ticks++;
            };

            timer.Enabled = true;
            PumpUntil(() => ticks > 0);

            Assert.True(ticks > 0, "no Tick arrived");
            Assert.Same(timer, sender);
        });
    }

    [Fact]
    public void Dispose_stops_ticks()
    {
        Sta.Run(() =>
        {
            var timer = new WinFormsSampleTimer { IntervalMs = 5 };
            var ticks = 0;
            timer.Tick += (_, _) => ticks++;
            timer.Enabled = true;
            PumpUntil(() => ticks > 0);
            Assert.True(ticks > 0, "no Tick arrived before Dispose");

            timer.Dispose();
            var before = ticks;
            PumpUntil(() => false, timeoutMs: 100);

            Assert.Equal(before, ticks);
        });
    }
}
