<#
.SYNOPSIS
    Generates steady process churn for the tmcs leak soak.
.DESCRIPTION
    Keeps -Parallel short-lived "cmd.exe /c ping -n 2 127.0.0.1" processes running for -Minutes, starting a new one as each
    exits. Each lives about a second. They start without a window (UseShellExecute off, CreateNoWindow on) and their output
    goes to nul. When the time is up no new ones start; the script waits for the rest, then prints how many it started.
.EXAMPLE
    pwsh -File tools/churn.ps1 -Parallel 8 -Minutes 52
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 256)]
    [int] $Parallel = 8,

    [ValidateRange(0.01, 10080)]
    [double] $Minutes = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Start-Churner {
    $info = [System.Diagnostics.ProcessStartInfo]::new('cmd.exe', '/c ping -n 2 127.0.0.1 >nul 2>&1')
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    [System.Diagnostics.Process]::Start($info)
}

$deadline = [DateTime]::UtcNow.AddMinutes($Minutes)
$running = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$spawned = 0
try {
    while ([DateTime]::UtcNow -lt $deadline) {
        for ($i = $running.Count - 1; $i -ge 0; $i--) {
            if ($running[$i].HasExited) {
                $running[$i].Dispose()
                $running.RemoveAt($i)
            }
        }
        while ($running.Count -lt $Parallel) {
            $running.Add((Start-Churner))
            $spawned++
        }
        Start-Sleep -Milliseconds 50
    }
    foreach ($process in $running) { $process.WaitForExit() }
}
finally {
    foreach ($process in $running) { $process.Dispose() }
}

"Spawned $spawned processes in $Minutes minutes, $Parallel at a time."
