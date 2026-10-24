using System.Diagnostics;
using Tmcs.Native;
using Tmcs.Processes;

namespace Tmcs;

static class Program
{
    private const string SingleInstanceMutex = @"Local\tmcs.single-instance";

    /// <summary>True while the error dialog is up. Another exception meanwhile, such as the next timer tick's, is dropped rather than stacking a second dialog.</summary>
    private static bool _showingError;

    [STAThread]
    static void Main()
    {
        // Read first: test mode skips the single-instance check.
        var options = LaunchOptions.FromProcessEnvironment();
        using var instance = options.IsTestMode ? null : ClaimSingleInstance(); // held until tmcs exits
        if (!options.IsTestMode && instance is null)
        {
            ActivateRunningInstance();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += OnThreadException;
        ProcessInfoSource.TryEnableDebugPrivilege(); // only succeeds when elevated
        Application.Run(new MainForm(AppServices.CreateReal(options)));
    }

    /// <returns>The mutex that marks this process as the running instance, or null when another instance exists.</returns>
    private static Mutex? ClaimSingleInstance()
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: false, SingleInstanceMutex, out var createdNew);
            if (createdNew) return mutex;
        }
        catch (UnauthorizedAccessException)
        {
            return null; // it exists, created by an elevated instance this one may not open
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Brings the other instance's window forward, restoring it only if it is minimized: SW_RESTORE would un-maximize a maximized window.</summary>
    private static void ActivateRunningInstance()
    {
        var self = Environment.ProcessId;
        var processes = Process.GetProcessesByName("tmcs");
        try
        {
            foreach (var process in processes)
            {
                nint window;
                try
                {
                    window = process.Id == self ? 0 : process.MainWindowHandle;
                }
                catch (InvalidOperationException)
                {
                    continue; // it exited meanwhile
                }

                if (window == 0) continue;
                if (User32.IsIconic(window)) User32.ShowWindow(window, User32.SwRestore);
                User32.SetForegroundWindow(window);
                return;
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        if (_showingError) return;
        _showingError = true;
        try
        {
            using var dialog = new ThreadExceptionDialog(e.Exception);
            if (dialog.ShowDialog() == DialogResult.Abort) Application.Exit();
        }
        finally
        {
            _showingError = false;
        }
    }
}
