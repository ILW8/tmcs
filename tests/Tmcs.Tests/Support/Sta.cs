using System.Runtime.ExceptionServices;

namespace Tmcs.Tests;

/// <summary>Runs test bodies that touch WinForms on a fresh STA thread.</summary>
internal static class Sta
{
    /// <summary>Runs <paramref name="body"/> on a new STA thread, waits for it, and rethrows anything it threw.</summary>
    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
