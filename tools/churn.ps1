<#
.SYNOPSIS
    Generates steady process churn for the tmcs leak soak.
.DESCRIPTION
    Keeps -Parallel short-lived "cmd.exe /c ping -n 2 127.0.0.1" processes running for -Minutes, starting a new one as each
    exits. Each lives about a second. They start without a window (UseShellExecute off, CreateNoWindow on) and their output
    goes to nul. When the time is up no new ones start; the script waits for the rest, then prints how many it started. A
    process still running 10 s after the end is killed, with its children, and a warning names it.

    With -GpuParallel above 0 it also keeps that many gpu-touch processes running beside them, each replaced as it exits, so
    the GPU's per-process counter instances churn too. gpu-touch (tools/gpu-touch) creates a Direct3D 11 device, holds it for
    500 ms and exits with 0, or with 1 if it could not create the device; it opens no window. Build it first with
    "dotnet build tools/gpu-touch -c Release", or pass its path with -GpuTouch. The final line then adds how many gpu-touch
    processes it started and how many failed (a nonzero exit code). After 10 failures in a row the script stops with an
    error, since a soak without GPU churn would prove nothing.
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

# A gpu-touch that fails (no hardware device, a driver reset) creates no GPU instances, so the soak's GPU check would pass on
# a flat count that proves nothing. This many failures in a row stop the script with an error; a success resets the run.
$MaxGpuFailuresInARow = 10

# At the end, each process left gets this long to exit before it is killed with its children, so a gpu-touch stuck in
# D3D11CreateDevice cannot hang the script.
$EndWaitMs = 10000

$gpuFailed = 0
$gpuFailedInARow = 0
$gpuLastExitCode = 0

# Counts a finished gpu-touch: a nonzero exit code is a failure.
$onGpuExit = {
    param([System.Diagnostics.Process] $Process)
    $script:gpuLastExitCode = $Process.ExitCode
    if ($Process.ExitCode -ne 0) {
        $script:gpuFailed++
        $script:gpuFailedInARow++
    }
    else {
        $script:gpuFailedInARow = 0
    }
}

# Drops the exited processes from $Pool, passing each to $OnExit if given, then starts new ones with $Start until it holds
# $Size. Returns how many it started.
function Update-Pool([System.Collections.Generic.List[System.Diagnostics.Process]] $Pool, [int] $Size, [scriptblock] $Start, [scriptblock] $OnExit) {
    for ($i = $Pool.Count - 1; $i -ge 0; $i--) {
        if ($Pool[$i].HasExited) {
            if ($null -ne $OnExit) { & $OnExit $Pool[$i] }
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

# Waits up to $EndWaitMs for each process in $Pool, kills one still running then with its children, and passes each to $OnExit
# if given.
function Wait-Pool([System.Collections.Generic.List[System.Diagnostics.Process]] $Pool, [scriptblock] $OnExit) {
    foreach ($process in $Pool) {
        if (-not $process.WaitForExit($EndWaitMs)) {
            Write-Warning "Process $($process.Id) still ran $($EndWaitMs / 1000) s after the churn ended, so it was killed."
            $process.Kill($true)
            $process.WaitForExit()
        }
        if ($null -ne $OnExit) { & $OnExit $process }
    }
}

$deadline = [DateTime]::UtcNow.AddMinutes($Minutes)
$running = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$gpuRunning = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$spawned = 0
$gpuSpawned = 0
try {
    while ([DateTime]::UtcNow -lt $deadline -and $gpuFailedInARow -lt $MaxGpuFailuresInARow) {
        $spawned += Update-Pool $running $Parallel ${function:Start-Churner} $null
        $gpuSpawned += Update-Pool $gpuRunning $GpuParallel ${function:Start-GpuToucher} $onGpuExit
        Start-Sleep -Milliseconds 50
    }
    Wait-Pool $running $null
    Wait-Pool $gpuRunning $onGpuExit
}
finally {
    foreach ($process in $running) { $process.Dispose() }
    foreach ($process in $gpuRunning) { $process.Dispose() }
}

if ($gpuFailedInARow -ge $MaxGpuFailuresInARow) {
    throw "gpu-touch failed $gpuFailedInARow times in a row (last exit code $gpuLastExitCode), so the GPU's counter instances are not churning. Spawned $gpuSpawned gpu-touch processes, $gpuFailed failed."
}

$summary = "Spawned $spawned processes in $Minutes minutes, $Parallel at a time."
if ($GpuParallel -gt 0) { $summary += " Spawned $gpuSpawned gpu-touch processes, $GpuParallel at a time, $gpuFailed failed." }
$summary
