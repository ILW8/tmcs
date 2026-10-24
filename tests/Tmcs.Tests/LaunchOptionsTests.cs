namespace Tmcs.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void Reads_diag_and_scenario_variables()
    {
        var variables = new Dictionary<string, string?> { ["TMCS_DIAG"] = @"c:\t\d.csv", ["TMCS_SCENARIO"] = "details:1" };

        var options = LaunchOptions.FromEnvironment(name => variables.GetValueOrDefault(name));

        Assert.Equal(@"c:\t\d.csv", options.DiagPath);
        Assert.Equal("details:1", options.Scenario);
        Assert.True(options.IsTestMode);
        Assert.Equal("TMCS_DIAG", LaunchOptions.DiagVariable);
        Assert.Equal("TMCS_SCENARIO", LaunchOptions.ScenarioVariable);
    }

    [Fact]
    public void Unset_or_blank_variables_are_null()
    {
        var none = LaunchOptions.FromEnvironment(_ => null);
        Assert.Null(none.DiagPath);
        Assert.Null(none.Scenario);
        Assert.False(none.IsTestMode);

        var blank = new Dictionary<string, string?> { ["TMCS_DIAG"] = "  ", ["TMCS_SCENARIO"] = "" };
        var options = LaunchOptions.FromEnvironment(name => blank.GetValueOrDefault(name));
        Assert.Null(options.DiagPath);
        Assert.Null(options.Scenario);
        Assert.False(options.IsTestMode);
    }

    [Fact]
    public void Process_environment_variables_are_cleared_after_reading()
    {
        try
        {
            Environment.SetEnvironmentVariable("TMCS_DIAG", @"c:\t\d.csv");
            Environment.SetEnvironmentVariable("TMCS_SCENARIO", "details:1");

            var options = LaunchOptions.FromProcessEnvironment();

            Assert.Equal(@"c:\t\d.csv", options.DiagPath);
            Assert.Equal("details:1", options.Scenario);
            Assert.Null(Environment.GetEnvironmentVariable("TMCS_DIAG"));
            Assert.Null(Environment.GetEnvironmentVariable("TMCS_SCENARIO"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMCS_DIAG", null);
            Environment.SetEnvironmentVariable("TMCS_SCENARIO", null);
        }
    }
}
