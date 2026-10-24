using System.Diagnostics;
using System.Security.Principal;
using Tmcs.Processes;

namespace Tmcs.Tests.Processes;

public class ProcessInfoSourceTests
{
    static ProcessIdentity Me()
    { var l = new List<RawProcess>(); new SystemProcessReader().Read(l); var r = l.Single(x => x.Pid == Environment.ProcessId); return new(r.Pid, r.CreateTime); }
    static ProcessInfoSource Src() => new(new SidNameCache(SidNameCache.LookupAccountName));

    [Fact] public void Query_current_process_returns_path_command_line_user()
    { var i = Src().Query(Me());
      Assert.Equal(Environment.ProcessPath, i.ImagePath, ignoreCase: true);
      Assert.Contains(Path.GetFileName(Environment.ProcessPath!), i.CommandLine!, StringComparison.OrdinalIgnoreCase);
      Assert.Equal(Environment.UserName, i.UserName, ignoreCase: true);
      Assert.NotEqual(UacVirtualization.Unknown, i.Uac); }

    [Fact] public void Query_system_and_idle_report_SYSTEM()
    { var s = Src(); Assert.Equal("SYSTEM", s.Query(new(4, 0)).UserName); Assert.Equal("SYSTEM", s.Query(new(0, 0)).UserName); Assert.Null(s.Query(new(4, 0)).ImagePath); }

    [Fact] public void Query_mismatched_identity_returns_blanks()
    { var me = Me(); Assert.Equal(ProcessStaticInfo.Blank, Src().Query(me with { CreateTime = me.CreateTime + 1 }));
      Assert.Equal(ProcessStaticInfo.Blank, Src().Query(new(0x7FFFFFF0, 1))); }

    [Fact] public void Query_does_not_leak_handles()
    { var l = new List<RawProcess>(); new SystemProcessReader().Read(l); var s = Src();
      var before = Process.GetCurrentProcess().HandleCount;
      for (int n = 0; n < 5; n++) foreach (var r in l) s.Query(new(r.Pid, r.CreateTime));
      Process.GetCurrentProcess().Refresh(); Assert.InRange(Process.GetCurrentProcess().HandleCount, 0, before + 20); }

    [Fact] public void Query_reads_a_command_line_longer_than_the_first_buffer()
    {
        // The marker sits at the very end, so a truncated or mis-sized second read would lose it.
        const string marker = "END-OF-COMMAND-LINE";
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul & rem " + new string('x', 6000) + marker)
        { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            var l = new List<RawProcess>(); new SystemProcessReader().Read(l);
            var r = l.Single(x => x.Pid == child.Id);
            var i = Src().Query(new(r.Pid, r.CreateTime));
            Assert.EndsWith(marker, i.CommandLine);
            Assert.True(i.CommandLine!.Length > 6000);
        }
        finally { child.Kill(entireProcessTree: true); child.WaitForExit(); }
    }

    [Fact] public void Query_never_throws_even_when_the_sid_lookup_does()
    { var s = new ProcessInfoSource(new SidNameCache(_ => throw new InvalidOperationException("boom")));
      Assert.Equal(ProcessStaticInfo.Blank, s.Query(Me())); }

    [Fact] public void TryEnableDebugPrivilege_never_throws_and_succeeds_when_elevated()
    { var granted = ProcessInfoSource.TryEnableDebugPrivilege(); // false is the normal answer for a non-elevated run
      if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) Assert.True(granted); }
}
