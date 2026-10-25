<#
.SYNOPSIS
    Generates steady process churn for the tmcs leak soak.
.DESCRIPTION
    Keeps -Parallel short-lived "cmd.exe /c ping -n 2 127.0.0.1" processes running for -Minutes, starting a new one as each
    exits. Each lives about a second. They start without a window (UseShellExecute off, CreateNoWindow on) and their output
    goes to nul. When the time is up no new ones start; the script waits for the rest, then prints how many it started.

    With -GpuParallel above 0 it also keeps that many gpu-touch processes running beside them, each replaced as it exits, so
    the GPU's per-process counter instances churn too. gpu-touch (tools/gpu-touch) creates a Direct3D 11 device, holds it for
    500 ms and exits; it opens no window. Build it first with "dotnet build tools/gpu-touch -c Release", or pass its path
    with -GpuTouch. The final line then adds how many gpu-touch processes it started.
.EXAMPLE
    pwsh -File tools/churn.ps1 -Parallel 8 -Minutes 52
.EXAMPLE
    pwsh -File tools/churn.ps1 -Parallel 8 -GpuParallel 2 -Minutes 80
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 256)]
    [int] $Parallel = 8,

    [ValidateRange(0.01, 10080)]
    [double] $Minutes = 60,

    [ValidateRange(0, 64)]
    [int] $GpuParallel = 0,

    [string] $GpuTouch = (Join-Path $PSScriptRoot 'gpu-touch/bin/Release/net10.0-windows/win-x64/gpu-touch.exe')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($GpuParallel -gt 0) {
    if (-not (Test-Path -LiteralPath $GpuTouch -PathType Leaf)) {
        throw "gpu-touch was not found at $GpuTouch. Build it with: dotnet build tools/gpu-touch -c Release"
    }
    $GpuTouch = (Resolve-Path -LiteralPath $GpuTouch).ProviderPath
}

function Start-Churner {
    $info = [System.Diagnostics.ProcessStartInfo]::new('cmd.exe', '/c ping -n 2 127.0.0.1 >nul 2>&1')
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    [System.Diagnostics.Process]::Start($info)
}

function Start-GpuToucher {
    $info = [System.Diagnostics.ProcessStartInfo]::new($GpuTouch)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    [System.Diagnostics.Process]::Start($info)
}

# Drops the exited processes from $Pool, then starts new ones with $Start until it holds $Size. Returns how many it started.
function Update-Pool([System.Collections.Generic.List[System.Diagnostics.Process]] $Pool, [int] $Size, [scriptblock] $Start) {
    for ($i = $Pool.Count - 1; $i -ge 0; $i--) {
        if ($Pool[$i].HasExited) {
            $Pool[$i].Dispose()
            $Pool.RemoveAt($i)
        }
    }
    $started = 0
    while ($Pool.Count -lt $Size) {
        $Pool.Add((& $Start))
        $started++
    }
    $started
}

$deadline = [DateTime]::UtcNow.AddMinutes($Minutes)
$running = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$gpuRunning = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$spawned = 0
$gpuSpawned = 0
try {
    while ([DateTime]::UtcNow -lt $deadline) {
        $spawned += Update-Pool $running $Parallel ${function:Start-Churner}
        $gpuSpawned += Update-Pool $gpuRunning $GpuParallel ${function:Start-GpuToucher}
        Start-Sleep -Milliseconds 50
    }
    foreach ($process in $running) { $process.WaitForExit() }
    foreach ($process in $gpuRunning) { $process.WaitForExit() }
}
finally {
    foreach ($process in $running) { $process.Dispose() }
    foreach ($process in $gpuRunning) { $process.Dispose() }
}

$summary = "Spawned $spawned processes in $Minutes minutes, $Parallel at a time."
if ($GpuParallel -gt 0) { $summary += " Spawned $gpuSpawned gpu-touch processes, $GpuParallel at a time." }
$summary
