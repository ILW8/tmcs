<#
.SYNOPSIS
    Captures the stock Task Manager's Performance tab as the reference for tmcs's copy of it.
.DESCRIPTION
    Run it elevated: Task Manager runs elevated, and Windows hides an elevated window's contents from a non-elevated script.
    Task Manager must be open and not minimized; it may sit behind other windows. The script reads and drives Task Manager
    through MSAA (oleacc), which reaches its DirectUI elements; the managed UI Automation client in PowerShell sees only
    window-level panes. It:
    - dumps Task Manager's accessibility tree (role, name, value, state, bounds relative to the window, default action);
    - selects the Performance tab, then each sidebar entry in turn, and after each one saves a PrintWindow capture (PNG) and a
      tree dump;
    - on the CPU pane, logs Task Manager's Utilization and Speed at each of its updates (found by its Up time changing) next to
      the Processor Information(_Total) counters % Processor Utility, % Processor Time, % Processor Performance and Processor
      Frequency, read over the same interval, and reports which counter Task Manager's Utilization follows;
    - selects again the tab and sidebar entry that were selected when it started.
    It never moves the mouse, sends keystrokes or activates a window. Selecting a tab or pane does change what Task Manager
    shows. Everything goes to -OutDir, with a log.txt. The captures show Microsoft's UI: reference only, never commit them
    (artifacts/ is git-ignored).
    -Only limits the captures to some sidebar entries. -WindowHandle points it at another window instead, for a dry run: tree
    dump and capture only, nothing selected.
.EXAMPLE
    Start-Process pwsh -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile', '-File', "$PWD\tools\capture-taskmgr.ps1", '-OutDir', "$PWD\artifacts\reference\taskmgr"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutDir,

    [ValidateRange(0, 600)]
    [int] $CounterSeconds = 60,

    [ValidateRange(200, 10000)]
    [int] $SettleMs = 2500,

    # Captures only the sidebar entries whose "title sub" label matches this regex, for example 'GPU'.
    [string] $Only = '',

    [long] $WindowHandle = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing.Common
# Accessibility.dll targets .NET Framework; its System.Runtime 4.0 reference warns (CS1701) but binds to the running one.
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -ReferencedAssemblies Accessibility, System.Collections, System.Runtime.InteropServices, System.Threading.Thread -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Accessibility;

public sealed class MsaaNode
{
    public IAccessible Acc;   // the object that answers for this node
    public int ChildId;       // 0 when Acc is the node itself, else a simple element of Acc
    public int Depth;
    public string Name, Value, Role, Description, DefaultAction;
    public int State, X, Y, W, H;
    public bool Selected { get { return (State & 0x2) != 0; } }
    public int Bottom { get { return Y + H; } }
}

public static class Msaa
{
    public const int Invisible = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("oleacc.dll")]
    static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object acc);
    [DllImport("oleacc.dll")]
    static extern int AccessibleChildren(IAccessible parent, int start, int count, [Out] object[] children, out int obtained);
    [DllImport("oleacc.dll", CharSet = CharSet.Unicode)]
    static extern uint GetRoleTextW(uint role, StringBuilder text, uint max);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string title);

    static Guid IidIAccessible = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");

    static string Safe(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }

    public static void Refresh(MsaaNode n)
    {
        object id = n.ChildId;
        var acc = n.Acc;
        n.Name = Safe(() => acc.get_accName(id));
        n.Value = Safe(() => acc.get_accValue(id));
        n.Description = Safe(() => acc.get_accDescription(id));
        n.DefaultAction = Safe(() => acc.get_accDefaultAction(id));
        try { object s = acc.get_accState(id); n.State = s is int ? (int)s : 0; } catch { n.State = 0; }
        try
        {
            object r = acc.get_accRole(id);
            if (r is int) { var sb = new StringBuilder(64); GetRoleTextW((uint)(int)r, sb, 64); n.Role = sb.ToString(); }
            else n.Role = r as string ?? "";
        }
        catch { n.Role = ""; }
        try { int x, y, w, h; acc.accLocation(out x, out y, out w, out h, id); n.X = x; n.Y = y; n.W = w; n.H = h; } catch { }
    }

    static MsaaNode Describe(IAccessible acc, int childId, int depth)
    {
        var n = new MsaaNode { Acc = acc, ChildId = childId, Depth = depth };
        Refresh(n);
        return n;
    }

    /// <summary>The window's client object, walked depth first. Invisible nodes are skipped with their subtrees.</summary>
    public static List<MsaaNode> Walk(IntPtr hwnd, int maxNodes)
    {
        object o;
        int hr = AccessibleObjectFromWindow(hwnd, 0xFFFFFFFC /* OBJID_CLIENT */, ref IidIAccessible, out o);
        if (hr != 0) throw new COMException("AccessibleObjectFromWindow failed", hr);
        var list = new List<MsaaNode>();
        var stack = new Stack<MsaaNode>();
        stack.Push(Describe((IAccessible)o, 0, 0));
        while (stack.Count > 0 && list.Count < maxNodes)
        {
            var n = stack.Pop();
            if ((n.State & Invisible) != 0) continue;
            list.Add(n);
            if (n.ChildId != 0) continue; // a simple element has no children
            int count = 0;
            try { count = n.Acc.accChildCount; } catch { }
            if (count <= 0) continue;
            var kids = new object[count];
            int got;
            if (AccessibleChildren(n.Acc, 0, count, kids, out got) != 0) continue;
            var nodes = new List<MsaaNode>();
            for (int i = 0; i < got; i++)
            {
                var a = kids[i] as IAccessible;
                if (a != null) nodes.Add(Describe(a, 0, n.Depth + 1));
                else if (kids[i] is int) nodes.Add(Describe(n.Acc, (int)kids[i], n.Depth + 1));
            }
            for (int i = nodes.Count - 1; i >= 0; i--) stack.Push(nodes[i]);
        }
        return list;
    }

    /// <summary>The default action (a sidebar button's "Press"), or null when it throws. Doesn't check what it did.</summary>
    public static string Press(MsaaNode n)
    {
        try { n.Acc.accDoDefaultAction((object)n.ChildId); return "accDoDefaultAction"; }
        catch { return null; }
    }

    /// <summary>accSelect(SELFLAG_TAKESELECTION), then the default action. Returns how it was selected, or null.</summary>
    public static string Select(MsaaNode n, int settleMs)
    {
        object id = n.ChildId;
        try
        {
            n.Acc.accSelect(2, id);
            System.Threading.Thread.Sleep(settleMs);
            Refresh(n);
            if (n.Selected) return "accSelect";
        }
        catch { }
        try
        {
            n.Acc.accDoDefaultAction(id);
            System.Threading.Thread.Sleep(settleMs);
            Refresh(n);
            if (n.Selected) return "accDoDefaultAction";
        }
        catch { }
        return null;
    }
}
'@

# Physical pixels everywhere, whatever the monitor's scale.
[void][Msaa]::SetThreadDpiAwarenessContext([IntPtr]-4)

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$LogPath = Join-Path $OutDir 'log.txt'
$script:Failures = 0
$DevicePattern = '^(CPU|Memory|Disk \d|Ethernet|Wi-Fi|GPU \d|Bluetooth)'

function Write-Log([string] $text) {
    $line = '{0:HH:mm:ss.fff} {1}' -f [DateTime]::UtcNow, $text
    Add-Content -Path $LogPath -Value $line
    Write-Host $line
}

function Write-Failure([string] $text) {
    $script:Failures++
    Write-Log "FAIL: $text"
}

function Format-Flat([string] $text) { ($text -replace "`r?`n", '\n') }

function Get-SafeName([string] $name) {
    $flat = ($name -replace '\s+', ' ').Trim()
    if ($flat.Length -gt 40) { $flat = $flat.Substring(0, 40) }
    ($flat -replace '[^A-Za-z0-9 ()._-]', '_').Trim()
}

function Get-WindowRect {
    $r = [Msaa+RECT]::new()
    if (-not [Msaa]::GetWindowRect($hwnd, [ref] $r)) { throw 'GetWindowRect failed' }
    $r
}

function Get-Nodes { [Msaa]::Walk($hwnd, 20000) }

function Save-Tree($nodes, [string] $path) {
    $r = Get-WindowRect
    $nodes | ForEach-Object {
        '{0}{1} "{2}" value="{3}" state=0x{4:X} rect={5},{6},{7},{8} action="{9}" desc="{10}"' -f ('  ' * $_.Depth), $_.Role,
            (Format-Flat $_.Name), (Format-Flat $_.Value), $_.State, ($_.X - $r.Left), ($_.Y - $r.Top), $_.W, $_.H,
            $_.DefaultAction, (Format-Flat $_.Description)
    } | Set-Content -Encoding utf8 -Path $path
}

# PW_RENDERFULLCONTENT (2): the DWM-composed image, which also works while the window is covered. Returns an error or $null.
function Save-WindowImage([string] $path) {
    $r = Get-WindowRect
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { return 'empty window rectangle' }
    $bmp = [System.Drawing.Bitmap]::new($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppRgb)
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $hdc = $g.GetHdc()
            try { $ok = [Msaa]::PrintWindow($hwnd, $hdc, 2) } finally { $g.ReleaseHdc($hdc) }
        }
        finally { $g.Dispose() }
        if (-not $ok) { return 'PrintWindow failed' }
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $null
    }
    finally { $bmp.Dispose() }
}

function Save-Pane([int] $index, [string] $name) {
    $base = Join-Path $OutDir ('{0:D2}-{1}' -f $index, (Get-SafeName $name))
    $err = Save-WindowImage "$base.png"
    if ($err) { Write-Failure "capture of '$name': $err" }
    Save-Tree (Get-Nodes) "$base.txt"
    Write-Log "saved $base.png / .txt"
}

function Select-Node($node, [string] $what) {
    $how = [Msaa]::Select($node, 300)
    if ($how) { Write-Log "selected '$what' through $how"; return $true }
    Write-Failure "could not select '$what'"
    $false
}

# The node just below a label and left-aligned with it: how Task Manager lays out its big numbers.
function Find-ValueBelow($nodes, [string] $label) {
    $l = $nodes | Where-Object { $_.Name -eq $label } | Select-Object -First 1
    if (-not $l) { return $null }
    $nodes | Where-Object { $_.Name -and $_.Name -ne $label -and $_.Y -ge $l.Bottom - 2 -and $_.Y -le $l.Bottom + 40 -and [Math]::Abs($_.X - $l.X) -le 12 } |
        Sort-Object Y | Select-Object -First 1
}

function Measure-CpuCounters([int] $seconds) {
    $nodes = @(Get-Nodes)
    $util = Find-ValueBelow $nodes 'Utilization'
    $speed = Find-ValueBelow $nodes 'Speed'
    $uptime = Find-ValueBelow $nodes 'Up time'
    if (-not $util -or -not $uptime) { Write-Failure 'CPU pane: no Utilization or Up time value found'; return }
    Write-Log "CPU pane values: Utilization='$($util.Name)' Speed='$(if ($speed) { $speed.Name })' Up time='$($uptime.Name)'"

    $counters = [ordered]@{}
    foreach ($n in '% Processor Utility', '% Processor Time', '% Processor Performance', 'Processor Frequency') {
        $counters[$n] = [System.Diagnostics.PerformanceCounter]::new('Processor Information', $n, '_Total')
    }
    $csv = [System.Collections.Generic.List[string]]::new()
    $csv.Add('utc,tm_utilization,tm_speed,tm_uptime,utility,time,performance,frequency')
    $diffUtility = [System.Collections.Generic.List[double]]::new()
    $diffTime = [System.Collections.Generic.List[double]]::new()

    $last = $uptime.Name
    $started = $false
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 50
        [Msaa]::Refresh($uptime)
        if (-not $uptime.Name) { Write-Failure 'Up time value went away'; break }
        if ($uptime.Name -eq $last) { continue }
        $last = $uptime.Name
        # Task Manager just updated: read the counters over the same interval as its last update.
        $values = foreach ($c in $counters.Values) { $c.NextValue() }
        if (-not $started) { $started = $true; continue }  # the first read only starts the counters' interval
        [Msaa]::Refresh($util)
        if ($speed) { [Msaa]::Refresh($speed) }
        $s = if ($speed) { $speed.Name } else { '' }
        $csv.Add(('{0:o},"{1}","{2}","{3}",{4:F2},{5:F2},{6:F2},{7:F0}' -f [DateTime]::UtcNow, $util.Name, $s, $uptime.Name, $values[0], $values[1], $values[2], $values[3]))
        if ($util.Name -match '^(\d+)\s*%$') {
            $diffUtility.Add([Math]::Abs([double]$Matches[1] - $values[0]))
            $diffTime.Add([Math]::Abs([double]$Matches[1] - $values[1]))
        }
    }
    foreach ($c in $counters.Values) { $c.Dispose() }
    $csv | Set-Content -Encoding utf8 (Join-Path $OutDir 'cpu-counters.csv')
    if ($diffUtility.Count -gt 0) {
        $mu = ($diffUtility | Measure-Object -Average).Average
        $mt = ($diffTime | Measure-Object -Average).Average
        Write-Log ('CPU Utilization vs counters over {0} updates: mean |TM - % Processor Utility| = {1:F2}, mean |TM - % Processor Time| = {2:F2}' -f $diffUtility.Count, $mu, $mt)
    }
    else { Write-Failure 'CPU pane: no Utilization readings' }
}

# The direct children of $nodes[$index], from the depth-first list.
function Get-Children($nodes, [int] $index) {
    $depth = $nodes[$index].Depth
    for ($i = $index + 1; $i -lt $nodes.Count -and $nodes[$i].Depth -gt $depth; $i++) {
        if ($nodes[$i].Depth -eq $depth + 1) { $nodes[$i] }
    }
}

# The right-hand pane's heading: its first two text children, for example "CPU" / "AMD Ryzen 9 ..." or "Ethernet" / "Tailscale".
function Get-PaneHeading($nodes) {
    for ($i = 0; $i -lt $nodes.Count; $i++) {
        if ($nodes[$i].Role -eq 'pane' -and $nodes[$i].Name -eq 'Tables') {
            $texts = @(Get-Children $nodes $i | Where-Object { $_.Role -eq 'text' })
            return [pscustomobject]@{ Title = $(if ($texts.Count -gt 0) { $texts[0].Name.Trim() } else { '' }); Sub = $(if ($texts.Count -gt 1) { $texts[1].Name.Trim() } else { '' }) }
        }
    }
    [pscustomobject]@{ Title = ''; Sub = '' }
}

# Sidebar entries: the push buttons in the left third named like Task Manager's devices. Each one's heading is its first text
# child ("Ethernet"); a network entry also names its adapter in its first editable text child ("Tailscale"), which the pane's
# heading repeats.
function Find-SidebarEntries($nodes) {
    $r = Get-WindowRect
    $left = $r.Left + ($r.Right - $r.Left) / 3
    for ($i = 0; $i -lt $nodes.Count; $i++) {
        $n = $nodes[$i]
        if ($n.Role -ne 'push button' -or $n.Name -notmatch $DevicePattern -or $n.X -ge $left) { continue }
        $kids = @(Get-Children $nodes $i)
        $title = $kids | Where-Object { $_.Role -eq 'text' } | Select-Object -First 1
        $sub = $kids | Where-Object { $_.Role -eq 'editable text' } | Select-Object -First 1
        $network = $n.Description -like 'Send and receive*'
        [pscustomobject]@{
            Node = $n
            Title = $(if ($title) { $title.Name.Trim() } else { $n.Name })
            Sub = $(if ($network -and $sub) { $sub.Name.Trim() } else { '' })
        }
    }
}

# A pane may drop the entry's number from its title: sidebar "GPU 0", pane "GPU".
function Test-Title($heading, $entry) {
    $heading.Title -and ($heading.Title -eq $entry.Title -or $entry.Title -like "$($heading.Title) *")
}

function Test-Heading($heading, $entry) {
    (Test-Title $heading $entry) -and (-not $entry.Sub -or $heading.Sub -eq $entry.Sub)
}

# Presses a sidebar entry, then waits up to $SettleMs for the pane's heading to show it. A network pane may head itself with
# the adapter's description rather than its name, so a heading with the right title that changed since the press also counts.
function Show-Entry($entry) {
    $label = "$($entry.Title)$(if ($entry.Sub) { " / $($entry.Sub)" })"
    $before = Get-PaneHeading @(Get-Nodes)
    if (-not [Msaa]::Press($entry.Node)) { Write-Failure "could not press '$label'"; return $false }
    $deadline = [DateTime]::UtcNow.AddMilliseconds($SettleMs)
    do {
        Start-Sleep -Milliseconds 250
        $heading = Get-PaneHeading @(Get-Nodes)
        if (Test-Heading $heading $entry) { Write-Log "showing '$label'"; return $true }
    } while ([DateTime]::UtcNow -lt $deadline)
    $changed = $heading.Title -ne $before.Title -or $heading.Sub -ne $before.Sub
    if ((Test-Title $heading $entry) -and $changed) { Write-Log "showing '$label', headed '$($heading.Title) / $($heading.Sub)'"; return $true }
    Write-Failure "pressed '$label', but the pane shows '$($heading.Title) / $($heading.Sub)'"
    $false
}

# --- main ---------------------------------------------------------------------------------------------------------------------

$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Log "capture-taskmgr: elevated=$elevated out=$OutDir"
try {
    # [NullString]: PowerShell passes $null to a string parameter as "", which FindWindow takes as "an empty title".
    $hwnd = if ($WindowHandle -ne 0) { [IntPtr]$WindowHandle } else { [Msaa]::FindWindowW('TaskManagerWindow', [NullString]::Value) }
    if ($hwnd -eq [IntPtr]::Zero) { throw 'Task Manager is not open. Open it and run this again.' }
    if ([Msaa]::IsIconic($hwnd)) { throw 'The window is minimized. Restore it (it may stay behind other windows) and run this again.' }
    $r = Get-WindowRect
    Write-Log "window: $($r.Left),$($r.Top) to $($r.Right),$($r.Bottom), dpi $([Msaa]::GetDpiForWindow($hwnd))"
    if ($WindowHandle -eq 0) {
        Write-Log "Taskmgr.exe $((Get-Item (Join-Path ([Environment]::SystemDirectory) 'Taskmgr.exe')).VersionInfo.FileVersion)"
    }
    $nodes = @(Get-Nodes)
    Save-Tree $nodes (Join-Path $OutDir 'tree-start.txt')
    Save-Pane 0 'start'
    if ($WindowHandle -ne 0) { Write-Log 'dry run: done'; exit ([int]($script:Failures -gt 0)) }

    $tabs = @($nodes | Where-Object { $_.Role -eq 'page tab' })
    Write-Log "tabs: $(($tabs | ForEach-Object { '{0}{1}' -f $_.Name, $(if ($_.Selected) { '*' } else { '' }) }) -join ', ')"
    $originalTab = $tabs | Where-Object { $_.Selected } | Select-Object -First 1
    $performance = $tabs | Where-Object { $_.Name -eq 'Performance' } | Select-Object -First 1
    if (-not $performance) { throw 'No Performance tab found; see tree-start.txt.' }
    if (-not $performance.Selected) {
        if (-not (Select-Node $performance 'Performance tab')) { throw 'Stopped: the Performance tab could not be selected.' }
        Start-Sleep -Milliseconds $SettleMs
    }
    $nodes = @(Get-Nodes)
    Save-Tree $nodes (Join-Path $OutDir 'tree-performance.txt')

    $entries = @(Find-SidebarEntries $nodes)
    $original = Get-PaneHeading $nodes
    Write-Log "showing '$($original.Title) / $($original.Sub)'; sidebar entries: $($entries.Count): $(($entries | ForEach-Object { "$($_.Title)$(if ($_.Sub) { " / $($_.Sub)" })" }) -join ' | ')"
    if ($entries.Count -eq 0) { throw 'No sidebar entries found; see tree-performance.txt.' }

    $i = 1
    foreach ($entry in $entries) {
        if ($Only -and "$($entry.Title) $($entry.Sub)" -notmatch $Only) { $i++; continue }
        # The pane is up; one more second lets its numbers fill in before the capture.
        if (Show-Entry $entry) { Start-Sleep -Milliseconds 1000; Save-Pane $i "$($entry.Title) $($entry.Sub)" }
        $i++
    }

    if ($CounterSeconds -gt 0) {
        $cpu = $entries | Where-Object { $_.Title -eq 'CPU' } | Select-Object -First 1
        if ($cpu -and (Show-Entry $cpu)) { Start-Sleep -Milliseconds 1000; Measure-CpuCounters $CounterSeconds }
    }

    # Back to what was showing: the entry whose heading matches, the network entry with the same adapter when there is one.
    $back = $entries | Where-Object { (Test-Title $original $_) -and (-not $_.Sub -or $_.Sub -eq $original.Sub) } | Select-Object -First 1
    if ($back) { [void](Show-Entry $back) }
    if ($originalTab -and $originalTab.Name -ne 'Performance') { [void](Select-Node $originalTab 'the original tab') }
}
catch {
    Write-Failure $_.Exception.Message
}
Write-Log "done: $script:Failures failure(s)"
exit ([int]($script:Failures -gt 0))
