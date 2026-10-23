namespace Tmcs.Tests;

/// <summary>
/// A form that is shown far off-screen, never activated, and absent from the taskbar and Alt-Tab.
/// Tests that need a real window handle host their controls in one of these.
/// </summary>
internal class OffscreenForm : Form
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    public OffscreenForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>Shows the form (without activating it) and pumps pending messages once.</summary>
    public void ShowOffscreen()
    {
        Show();
        Application.DoEvents();
    }
}
