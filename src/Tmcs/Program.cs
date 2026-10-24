using System.Diagnostics;
using Tmcs.Diagnostics;
using Tmcs.Native;
using Tmcs.Processes;

namespace Tmcs;

static class Program
{
    private const string SingleInstanceMutex = @"Local\tmcs.single-instance";

    /// <summary>The exit code of a test run whose scenario does not parse.</summary>
    internal const int BadScenarioExitCode = 2;

    /// <summary>True while the error dialog is up. Another exception meanwhile, such as the next timer tick's, is dropped rather than stacking a second dialog.</summary>
    private static bool _showingError;

    [STAThread]
    static int Main()
    {
        // Read first: test mode skips the single-instance check.
        var options = LaunchOptions.FromProcessEnvironment();
        if (!CheckScenario(options)) return BadScenarioExitCode;

        using var instance = options.IsTestMode ? null : ClaimSingleInstance(); // held until tmcs exits
        if (!options.IsTestMode && instance is null)
        {
            ActivateRunningInstance();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += OnThreadException;
        ProcessInfoSource.TryEnableDebugPrivilege(); // only succeeds when elevated
        Application.Run(new MainForm(AppServices.CreateReal(options)));
        return 0;
    }

    /// <summary>
    /// Checks test mode's scenario before any window exists. A run nobody watches gets no dialog: a spec that does not parse
    /// leaves the parser's message in <c>DiagPath + ".error.txt"</c>, and tmcs exits with <see cref="BadScenarioExitCode"/>.
    /// Outside test mode the scenario is ignored.
    /// </summary>
    /// <returns>False when tmcs must exit with <see cref="BadScenarioExitCode"/>.</returns>
    internal static bool CheckScenario(LaunchOptions options)
    {
        if (options is not { IsTestMode: true, Scenario: { } spec }) return true;
        try
        {
            ScenarioParser.Parse(spec);
            return true;
        }
        catch (FormatException e)
        {
            var errorPath = options.DiagPath + ".error.txt";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(errorPath))!);
                File.WriteAllText(errorPath, e.Message + Environment.NewLine);
            }
            catch (Exception io) when (io is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine(io); // the exit code still reports the bad scenario
            }

            return false;
        }
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
