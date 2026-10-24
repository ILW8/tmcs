using System.Diagnostics;
using Tmcs.Diagnostics;
using Tmcs.Native;
using Tmcs.Processes;

namespace Tmcs;

static class Program
{
    private const string SingleInstanceMutex = @"Local\tmcs.single-instance";

    // Test mode's exit codes. Nobody watches a test run, so its failures show no dialog: each is reported on stderr and, unless
    // the TMCS_DIAG path itself was refused, in DiagPath + ".error.txt" (see ReportTestFailure).

    /// <summary>The exit code of a test run whose scenario does not parse.</summary>
    internal const int BadScenarioExitCode = 2;

    /// <summary>The exit code of a test run whose <c>TMCS_DIAG</c> path is refused (<see cref="DiagLog.Refusal"/>). Nothing is written to disk.</summary>
    internal const int DiagPathRefusedExitCode = 3;

    /// <summary>The exit code of a test run whose diagnostics CSV cannot be read, created or written, for example because it is locked.</summary>
    internal const int DiagLogFailedExitCode = 4;

    /// <summary>The exit code of a test run that ends on an unhandled exception, such as a timer tick's.</summary>
    internal const int RunFailedExitCode = 5;

    /// <summary>True while the error dialog is up. Another exception meanwhile, such as the next timer tick's, is dropped rather than stacking a second dialog.</summary>
    private static bool _showingError;

    /// <summary>Set by a test run's first unhandled exception, which ends the run: what <see cref="Main"/> returns. 0 otherwise.</summary>
    private static int _runExitCode;

    [STAThread]
    static int Main()
    {
        // Read first: test mode skips the single-instance check.
        var options = LaunchOptions.FromProcessEnvironment();
        var refused = CheckTestMode(options, Console.Error);
        if (refused != 0) return refused;

        using var instance = options.IsTestMode ? null : ClaimSingleInstance(); // held until tmcs exits
        if (!options.IsTestMode && instance is null)
        {
            ActivateRunningInstance();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => OnThreadException(options, e.Exception);
        ProcessInfoSource.TryEnableDebugPrivilege(); // only succeeds when elevated
        var form = CreateMainForm(options, () => new MainForm(AppServices.CreateReal(options)), Console.Error, out var failed);
        if (form is null) return failed;
        Application.Run(form);
        return _runExitCode;
    }

    /// <summary>
    /// Test mode's checks before any window exists: the <c>TMCS_DIAG</c> path (<see cref="DiagLog.Refusal"/>), then the scenario.
    /// A refused path is reported on stderr only, so nothing at all is written to disk. A file at the path that cannot be read,
    /// or a scenario that does not parse, is reported by <see cref="ReportTestFailure"/>. Outside test mode nothing is checked,
    /// and the scenario is ignored.
    /// </summary>
    /// <returns>0 to go on, or the exit code tmcs must exit with.</returns>
    internal static int CheckTestMode(LaunchOptions options, TextWriter stderr)
    {
        if (options.DiagPath is not { } path) return 0;
        try
        {
            if (DiagLog.Refusal(path) is { } refusal)
            {
                stderr.WriteLine(refusal);
                return DiagPathRefusedExitCode;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ReportTestFailure(path, $"tmcs could not read the diagnostics log: {e.Message}", stderr);
            return DiagLogFailedExitCode;
        }

        if (options.Scenario is not { } spec) return 0;
        try
        {
            ScenarioParser.Parse(spec);
            return 0;
        }
        catch (FormatException e)
        {
            ReportTestFailure(path, e.Message, stderr);
            return BadScenarioExitCode;
        }
    }

    /// <summary>
    /// Creates the main window with <paramref name="create"/>. In test mode its constructor opens the diagnostics CSV, and a CSV
    /// that cannot be created or written ends the run before any window shows: it is reported by <see cref="ReportTestFailure"/>,
    /// and the result is null with <see cref="DiagLogFailedExitCode"/>. Outside test mode every exception propagates.
    /// </summary>
    internal static MainForm? CreateMainForm(LaunchOptions options, Func<MainForm> create, TextWriter stderr, out int exitCode)
    {
        exitCode = 0;
        try
        {
            return create();
        }
        catch (Exception e) when (options.DiagPath is { } path && e is IOException or UnauthorizedAccessException)
        {
            ReportTestFailure(path, $"tmcs could not open the diagnostics log: {e.Message}", stderr);
            exitCode = DiagLogFailedExitCode;
            return null;
        }
    }

    /// <summary>
    /// Test mode's answer to an unhandled exception on the UI thread, such as a timer tick's: no dialog, since nobody watches the
    /// run. The exception is reported by <see cref="ReportTestFailure"/>, and the run must end with <see cref="RunFailedExitCode"/>.
    /// </summary>
    /// <returns>The exit code to end the run with, or null outside test mode, where the error dialog is shown.</returns>
    internal static int? TestRunFailure(LaunchOptions options, Exception e, TextWriter stderr)
    {
        if (options.DiagPath is not { } path) return null;
        ReportTestFailure(path, $"tmcs stopped on an unhandled exception: {e}", stderr);
        return RunFailedExitCode;
    }

    /// <summary>
    /// Reports a test-mode failure on stderr and in <c><paramref name="diagPath"/> + ".error.txt"</c>, creating its directory if
    /// needed. A path <see cref="DiagLog.Refusal"/> refuses never gets here. The error file is best effort: when it cannot be
    /// written either, stderr and the exit code still report the failure.
    /// </summary>
    private static void ReportTestFailure(string diagPath, string message, TextWriter stderr)
    {
        stderr.WriteLine(message);
        var errorPath = diagPath + ".error.txt";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(errorPath))!);
            File.WriteAllText(errorPath, message + Environment.NewLine);
        }
        catch (Exception io) when (io is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(io);
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

    /// <summary>
    /// Test mode reports the first exception and ends the run with its exit code (<see cref="TestRunFailure"/>); a later one,
    /// before the message loop has ended, is dropped. Otherwise the error dialog lets the owner continue or quit.
    /// </summary>
    private static void OnThreadException(LaunchOptions options, Exception exception)
    {
        if (_runExitCode != 0) return;
        if (TestRunFailure(options, exception, Console.Error) is { } exitCode)
        {
            _runExitCode = exitCode;
            Application.Exit();
            return;
        }

        if (_showingError) return;
        _showingError = true;
        try
        {
            using var dialog = new ThreadExceptionDialog(exception);
            if (dialog.ShowDialog() == DialogResult.Abort) Application.Exit();
        }
        finally
        {
            _showingError = false;
        }
    }
}
