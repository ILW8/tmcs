namespace Tmcs.Tests.Support;

public class StaTests
{
    [Fact]
    public void Run_surfaces_exception_thrown_by_a_handler_pumped_by_DoEvents()
    {
        // Without Sta.Run switching the thread to UnhandledExceptionMode.ThrowException, this exception would raise a
        // ThreadExceptionDialog on the user's screen instead of reaching the test. Do not remove that call to see this fail.
        var ex = Assert.Throws<InvalidOperationException>(() => Sta.Run(() =>
        {
            using var timer = new System.Windows.Forms.Timer { Interval = 1 };
            timer.Tick += (_, _) => throw new InvalidOperationException("from Tick");
            timer.Start();
            var deadline = Environment.TickCount64 + 5000;
            while (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(5);
                Application.DoEvents();
            }
            Assert.Fail("Tick handler never ran");
        }));
        Assert.Equal("from Tick", ex.Message);
    }
}
