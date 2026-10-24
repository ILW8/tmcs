using System.Diagnostics;
using System.Globalization;
using Tmcs.Diagnostics;

namespace Tmcs.Tests.Diagnostics;

public sealed class DiagnosticsTests : IDisposable
{
    // The directory does not exist until something writes into it.
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tmcs-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Parse_scenario_spec()
    {
        // A culture with a decimal comma: the minutes must still parse with the invariant culture.
        var steps = WithCulture("de-DE", () => ScenarioParser.Parse("details:10,minimized:0.5,paused:5"));

        Assert.Equal(
            [
                new ScenarioStep(ScenarioAction.Details, TimeSpan.FromMinutes(10)),
                new ScenarioStep(ScenarioAction.Minimized, TimeSpan.FromSeconds(30)),
                new ScenarioStep(ScenarioAction.Paused, TimeSpan.FromMinutes(5)),
            ],
            steps);
        Assert.Throws<FormatException>(() => ScenarioParser.Parse("bogus:1"));
        Assert.Throws<FormatException>(() => ScenarioParser.Parse("details"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("details:")]
    [InlineData("details:0")]
    [InlineData("details:-1")]
    [InlineData("details:1,")]
    [InlineData("details:1:2")]
    [InlineData("0:1")]
    [InlineData("details:NaN")]
    [InlineData("details:Infinity")]
    [InlineData("details:1e3")]
    [InlineData("details:40000")] // longer than a timer interval can hold (int.MaxValue ms is about 35791 minutes)
    public void Parse_rejects_bad_specs(string spec) => Assert.Throws<FormatException>(() => ScenarioParser.Parse(spec));

    [Fact]
    public void Runner_applies_steps_in_order_and_finishes()
    {
        var target = new RecordingTarget();
        var timer = new FakeTimer();
        var finished = 0;
        var runner = new ScenarioRunner(ScenarioParser.Parse("details:10,minimized:0.5,paused:5"), target, timer, () => finished++);

        timer.Fire(); // before Start: ignored
        Assert.Empty(target.Applied);
        Assert.Equal("none", runner.CurrentStep);

        runner.Start();
        Assert.Equal([ScenarioAction.Details], target.Applied);
        Assert.Equal("details", runner.CurrentStep);
        Assert.Equal(600_000, timer.IntervalMs);
        Assert.True(timer.Enabled);

        target.BusyMs = 20; // the next Apply takes at least 20 ms
        timer.Fire();
        Assert.Equal([ScenarioAction.Details, ScenarioAction.Minimized], target.Applied);
        Assert.Equal("minimized", runner.CurrentStep);
        Assert.Equal(30_000, timer.IntervalMs);
        Assert.InRange(runner.LastSwitchMs, 20, 5000);

        target.BusyMs = 0;
        timer.Fire();
        Assert.Equal([ScenarioAction.Details, ScenarioAction.Minimized, ScenarioAction.Paused], target.Applied);
        Assert.Equal("paused", runner.CurrentStep);
        Assert.Equal(300_000, timer.IntervalMs);
        Assert.InRange(runner.LastSwitchMs, 0, 20);
        Assert.Equal(0, finished);

        timer.Fire();
        Assert.Equal(1, finished);
        Assert.Equal("done", runner.CurrentStep);
        Assert.False(timer.Enabled);
        Assert.Equal(3, target.Applied.Count);

        timer.Fire(); // after the end: ignored
        Assert.Equal(1, finished);
        Assert.Equal(3, target.Applied.Count);
        Assert.Throws<InvalidOperationException>(runner.Start);
    }

    [Fact]
    public void Diag_format_is_invariant_csv()
    {
        var snapshot = new DiagSnapshot(new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc), "details", 5, 3, 4, 2, 1, 1, 1048576, 524288, 300, 40, 20, 12.5, 3.25);

        var line = WithCulture("de-DE", () => DiagLog.Format(snapshot));

        Assert.Equal("2026-10-23T12:00:00.0000000Z,details,5,3,4,2,1,1,1048576,524288,300,40,20,12.5,3.25", line);
        Assert.Equal(
            "utc,step,samples,rows,icon_slots,icon_live,icon_pooled,sid_cache,private_bytes,managed_bytes,handles,gdi,user,cpu_ms,switch_ms",
            DiagLog.Header);
        Assert.Equal(15, DiagLog.Header.Split(',').Length);
        Assert.Equal(15, line.Split(',').Length);
    }

    [Fact]
    public void Diag_log_writes_the_header_then_one_line_per_tick()
    {
        var path = Path.Combine(_dir, "nested", "diag.csv"); // its directory does not exist yet
        var timer = new FakeTimer();
        var probed = new List<DiagSnapshot>();

        DiagSnapshot Probe()
        {
            var n = probed.Count + 1;
            var snapshot = new DiagSnapshot(new DateTime(2026, 10, 23, 12, 0, 10 * n, DateTimeKind.Utc), "details", n, 3, 4, 2, 1, 1, 1048576 * n, 524288, 300, 40, 20, 12.5 * n, 3.25);
            probed.Add(snapshot);
            return snapshot;
        }

        using (new DiagLog(path, Probe, timer))
        {
            Assert.Equal(10_000, timer.IntervalMs);
            Assert.True(timer.Enabled);
            Assert.Equal([DiagLog.Header], ReadShared(path)); // written at once, readable while the log is open

            timer.Fire();
            Assert.Equal([DiagLog.Header, DiagLog.Format(probed[0])], ReadShared(path));
            timer.Fire();
            Assert.Equal([DiagLog.Header, DiagLog.Format(probed[0]), DiagLog.Format(probed[1])], ReadShared(path));
        }

        Assert.False(timer.Enabled);
        timer.Fire(); // after Dispose: ignored
        Assert.Equal(2, probed.Count);
        Assert.Equal(3, File.ReadAllLines(path).Length);

        // A new log on the same path starts over with the header.
        using (new DiagLog(path, Probe, new FakeTimer()))
        {
        }

        Assert.Equal([DiagLog.Header], File.ReadAllLines(path));
    }

    [Fact]
    public void Bad_scenario_is_refused_with_its_message_beside_the_csv()
    {
        var csv = Path.Combine(_dir, "soak", "run.csv"); // its directory does not exist yet

        Assert.False(Program.CheckScenario(new LaunchOptions(csv, "details:10,bogus:1")));

        Assert.Equal(2, Program.BadScenarioExitCode);
        Assert.Contains("bogus", File.ReadAllText(csv + ".error.txt"));
        Assert.False(File.Exists(csv)); // no log was started

        var good = Path.Combine(_dir, "good.csv");
        Assert.True(Program.CheckScenario(new LaunchOptions(good, "details:10,paused:0.5")));
        Assert.True(Program.CheckScenario(new LaunchOptions(good, null))); // test mode without a scenario
        Assert.True(Program.CheckScenario(new LaunchOptions(null, "bogus:1"))); // not test mode: the scenario is ignored
        Assert.False(File.Exists(good + ".error.txt"));
    }

    private static T WithCulture<T>(string name, Func<T> body)
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        try
        {
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>Reads every line while a writer may still hold the file open.</summary>
    private static List<string> ReadShared(string path)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines;
    }

    private sealed class RecordingTarget : IScenarioTarget
    {
        public readonly List<ScenarioAction> Applied = [];

        /// <summary>How long each Apply keeps busy, measured rather than slept, so the runner's stopwatch sees at least this.</summary>
        public int BusyMs;

        public void Apply(ScenarioAction a)
        {
            Applied.Add(a);
            var busy = Stopwatch.StartNew();
            while (busy.ElapsedMilliseconds < BusyMs)
            {
            }
        }
    }
}
