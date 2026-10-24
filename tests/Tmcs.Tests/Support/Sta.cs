using System.Runtime.ExceptionServices;

namespace Tmcs.Tests.Support;

/// <summary>Runs test bodies that touch WinForms on a fresh STA thread.</summary>
internal static class Sta
{
    /// <summary>
    /// Runs <paramref name="body"/> on a new STA thread, waits for it, and rethrows anything it threw.
    /// WinForms handler exceptions pumped by <c>Application.DoEvents</c> surface here too, instead of
    /// raising a ThreadExceptionDialog on the user's screen.
    /// </summary>
    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            // Must stay the first statement: it has to run before any control or window exists on this thread.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, threadScope: true);
            try
            {
                // Windows created in the body, and comctl32 imports first bound in it, come from comctl32 v6 as in the app.
                using (CommonControls6.Activate()) body();
            }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
