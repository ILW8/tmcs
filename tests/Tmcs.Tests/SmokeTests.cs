namespace Tmcs.Tests;

public class SmokeTests
{
    [Fact]
    public void MainForm_creates_offscreen_with_title_tmcs() => Sta.Run(() =>
    {
        using var f = new MainForm();
        f.CreateControl();
        Assert.Equal("tmcs", f.Text);
    });
}
