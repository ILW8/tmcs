namespace Tmcs;

internal sealed class MainForm : Form
{
    public MainForm()
    {
        Text = "tmcs";
        ClientSize = new Size(1294, 712);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
    }
}
