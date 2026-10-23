using Tmcs.Processes;

namespace Tmcs.Tests.Processes;

public class HungWindowScannerTests
{
    [Fact] public void HungWindowScanner_scan_excludes_current_process()
    { var hung = new HashSet<int>(); new HungWindowScanner().Scan(hung); Assert.DoesNotContain(Environment.ProcessId, hung); }

    [Fact] public void Scan_replaces_previous_contents()
    { var hung = new HashSet<int> { 0x7FFFFFF0 }; new HungWindowScanner().Scan(hung); Assert.DoesNotContain(0x7FFFFFF0, hung); }
}
