<#
.SYNOPSIS
    Checks a tmcs diagnostics log (the CSV TMCS_DIAG names) from a leak soak.
.DESCRIPTION
    A segment is a run of consecutive rows with the same step. Segments named done or none are not checked. In each other
    segment:
      - switch_ms on its first row (the switch into the step) is at most 100.
      - In a paused segment, samples does not change over the whole segment.
      - Over the rows from -WarmupMinutes after the segment's first row on: private bytes growth (the maximum of the last quarter
        of those rows minus the maximum of the first quarter) is at most 2 MB, and the gdi and user ranges (max - min) are at
        most 5 and the handles range at most 50. With fewer than 2 such rows these checks report SKIP.

    Same-state drift: a slow leak passes every segment on its own, so each state that occurs in more than one segment (done and
    none aside) compares its first segment with each later one. The first segment's rows are those from -WarmupMinutes on, as
    above; a later segment's rows are those from -SettleSeconds on, since a state switch settles fast. The later segment's
    median may exceed the first's by at most: private bytes 6 MB, handles 8, gdi 5 and user 5 (the per-segment gdi and user
    limits). A comparison where either side has fewer than -MinDriftRows rows reports SKIP as too short.
      - Calibrated on the two M1 soaks (artifacts/soak/m1-debug.csv and m1-elevated.csv, whose scenarios repeat details). Their
        later details segments drift 4.86 to 4.98 MB from the first at every warm-up from 2 to 9 minutes, but at most 0.04 MB
        from each other. The 4.9 MB is not a leak: it is the garbage collector's second heap commit at about minute 12
        (docs/superpowers/results/2026-10-23-m1-soak.md), after the first details segment ended at minute 10. The M2 scenario's
        first cpu segment ends at minute 12 too. So the limit is 6 MB, the tightest that passes both M1 soaks with a 1 MB
        margin. It can't catch 1 MB an hour while the first segment ends before the collector's last commit; measured from a
        segment after that commit, the M1 data would support 1 MB.
      - Handles drift -4 to +1 in the M1 soaks, and gdi and user -1 to 0. A limit of 8 handles passes them with 7 to spare and
        fails a leak of 9 or more.
    Whole run, after the table: the least-squares slopes of private bytes and handles per minute over every row after the first
    segment. They are for the record and not scored.

    When the CSV has the devices column (M2 on), devices is constant over the whole run; the check lists each change (row time,
    old -> new), since a change must be explained. When it has the gpu_instances column, a sampling segment is one whose samples
    advance, which leaves out paused and is decided from the data alone. The table lists gpu_instances' min/max in each sampling
    segment, and:
      - Churn: over the sampling segments' rows, max - min is at least -GpuTouchInstances.
      - No instance leak: a leak of PDH wildcard instances raises the floor, not the ceiling, so each later sampling segment's
        min is at most the first sampling segment's min + -GpuTouchInstances.
    Without those columns (M1 CSVs) their checks report SKIP.

    Prints a table with one row per check and exits 0 when no check FAILs, otherwise 1. INFO rows are values, not checks.
.PARAMETER SettleSeconds
    How long a later segment of a repeated state settles before its rows count toward drift.
.PARAMETER MinDriftRows
    The fewest rows each side of a drift comparison needs. Rows are 10 s apart, so the M2 scenario's cpu:2 has 9 after a 30 s
    settle; the default 6 (a minute of rows) lets it count.
.PARAMETER GpuTouchInstances
    The GPU Engine instances one gpu-touch process adds: 13 when Task 19 measured it. The first M2 soak confirms or corrects it.
.EXAMPLE
    pwsh -File tools/soak-report.ps1 -Csv artifacts/soak/m1-debug.csv
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Csv,

    [ValidateRange(0, 10080)]
    [double] $WarmupMinutes = 2,

    [ValidateRange(0, 86400)]
    [double] $SettleSeconds = 30,

    [ValidateRange(1, 100000)]
    [int] $MinDriftRows = 6,

    [ValidateRange(1, 100000)]
    [int] $GpuTouchInstances = 13
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$MaxSwitchMs = 100
$MaxPrivateGrowth = 2MB
$MaxGuiRange = 5
$MaxHandlesRange = 50
$MaxPrivateDrift = 6MB
$MaxHandlesDrift = 8
$Invariant = [cultureinfo]::InvariantCulture

$raw = @(Import-Csv -LiteralPath $Csv)
$columns = if ($raw.Count -gt 0) { @($raw[0].PSObject.Properties.Name) } else { @() }
$hasDevices = $columns -contains 'devices'
$hasGpuInstances = $columns -contains 'gpu_instances'

$rows = @($raw | ForEach-Object {
    [pscustomobject]@{
        Utc          = [datetime]::Parse($_.utc, $Invariant, [System.Globalization.DateTimeStyles]::RoundtripKind)
        Step         = $_.step
        Samples      = [long]::Parse($_.samples, $Invariant)
        PrivateBytes = [long]::Parse($_.private_bytes, $Invariant)
        Handles      = [long]::Parse($_.handles, $Invariant)
        Gdi          = [long]::Parse($_.gdi, $Invariant)
        User         = [long]::Parse($_.user, $Invariant)
        SwitchMs     = [double]::Parse($_.switch_ms, $Invariant)
        Devices      = if ($hasDevices) { [long]::Parse($_.devices, $Invariant) } else { 0 }
        GpuInstances = if ($hasGpuInstances) { [long]::Parse($_.gpu_instances, $Invariant) } else { 0 }
    }
})
if ($rows.Count -eq 0) {
    [Console]::Error.WriteLine("$Csv has no rows.")
    exit 1
}

$segments = [System.Collections.Generic.List[object]]::new()
foreach ($row in $rows) {
    if ($segments.Count -eq 0 -or $segments[$segments.Count - 1].Step -ne $row.Step) {
        $segments.Add([pscustomobject]@{ Index = $segments.Count + 1; Step = $row.Step; Rows = [System.Collections.Generic.List[object]]::new() })
    }
    $segments[$segments.Count - 1].Rows.Add($row)
}

$results = [System.Collections.Generic.List[object]]::new()

function Add-Result([object] $Segment, [string] $Check, [string] $Value, [string] $Threshold, [string] $Result) {
    $results.Add([pscustomobject]@{
        Segment   = $Segment.Index
        Step      = $Segment.Step
        Rows      = $Segment.Rows.Count
        Check     = $Check
        Value     = $Value
        Threshold = $Threshold
        Result    = $Result
    })
}

function Get-Verdict([bool] $Pass) { if ($Pass) { 'PASS' } else { 'FAIL' } }

function Format-Number([double] $Number, [string] $Format = '0.##') { $Number.ToString($Format, $Invariant) }

function Get-Median([object[]] $Rows, [string] $Property) {
    $sorted = @($Rows | ForEach-Object { [double]$_.$Property } | Sort-Object)
    $middle = [math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
}

function Get-RowsFrom([object] $Segment, [double] $Seconds) {
    $start = $Segment.Rows[0].Utc
    @($Segment.Rows | Where-Object { ($_.Utc - $start).TotalSeconds -ge $Seconds })
}

function Test-Checked([object] $Segment) { $Segment.Step -notin 'done', 'none' }

foreach ($segment in $segments) {
    if (-not (Test-Checked $segment)) {
        Add-Result $segment 'not checked' '' '' 'SKIP'
        continue
    }

    $first = $segment.Rows[0]
    Add-Result $segment 'switch_ms (first row)' (Format-Number $first.SwitchMs) "<= $MaxSwitchMs" (Get-Verdict ($first.SwitchMs -le $MaxSwitchMs))

    if ($segment.Step -eq 'paused') {
        if ($segment.Rows.Count -lt 2) {
            Add-Result $segment 'samples delta' '1 row' '== 0' 'SKIP'
        }
        else {
            $delta = $segment.Rows[$segment.Rows.Count - 1].Samples - $first.Samples
            Add-Result $segment 'samples delta' $delta '== 0' (Get-Verdict ($delta -eq 0))
        }
    }

    $warm = @($segment.Rows | Where-Object { ($_.Utc - $first.Utc).TotalMinutes -ge $WarmupMinutes })
    if ($warm.Count -lt 2) {
        foreach ($check in 'private bytes growth', 'gdi range', 'user range', 'handles range') {
            Add-Result $segment $check "$($warm.Count) rows after warm-up" 'needs 2' 'SKIP'
        }
        continue
    }

    $quarter = [math]::Max(1, [math]::Floor($warm.Count / 4))
    $early = ($warm | Select-Object -First $quarter | Measure-Object PrivateBytes -Maximum).Maximum
    $late = ($warm | Select-Object -Last $quarter | Measure-Object PrivateBytes -Maximum).Maximum
    $growth = $late - $early
    Add-Result $segment 'private bytes growth' "$(Format-Number ($growth / 1MB) '0.00') MB" '<= 2 MB' (Get-Verdict ($growth -le $MaxPrivateGrowth))

    foreach ($range in @(
            @{ Name = 'gdi range'; Property = 'Gdi'; Max = $MaxGuiRange },
            @{ Name = 'user range'; Property = 'User'; Max = $MaxGuiRange },
            @{ Name = 'handles range'; Property = 'Handles'; Max = $MaxHandlesRange })) {
        $measure = $warm | Measure-Object $range.Property -Minimum -Maximum
        $spread = $measure.Maximum - $measure.Minimum
        Add-Result $segment $range.Name $spread "<= $($range.Max)" (Get-Verdict ($spread -le $range.Max))
    }
}

# Same-state drift: each repeated state's first segment against each later one, as medians.
$drifts = @(
    @{ Name = 'private bytes'; Property = 'PrivateBytes'; Max = $MaxPrivateDrift; Unit = 1MB; Suffix = ' MB'; Format = '0.00' },
    @{ Name = 'handles'; Property = 'Handles'; Max = $MaxHandlesDrift; Unit = 1; Suffix = ''; Format = '0.#' },
    @{ Name = 'gdi'; Property = 'Gdi'; Max = $MaxGuiRange; Unit = 1; Suffix = ''; Format = '0.#' },
    @{ Name = 'user'; Property = 'User'; Max = $MaxGuiRange; Unit = 1; Suffix = ''; Format = '0.#' })
foreach ($group in @($segments | Where-Object { Test-Checked $_ } | Group-Object Step | Where-Object Count -GT 1)) {
    $firstSegment = $group.Group[0]
    $baseline = Get-RowsFrom $firstSegment ($WarmupMinutes * 60)
    foreach ($later in @($group.Group | Select-Object -Skip 1)) {
        $settled = Get-RowsFrom $later $SettleSeconds
        foreach ($drift in $drifts) {
            $check = "$($drift.Name) drift vs seg $($firstSegment.Index)"
            if ($baseline.Count -lt $MinDriftRows -or $settled.Count -lt $MinDriftRows) {
                Add-Result $later $check "too short: $($baseline.Count) and $($settled.Count) rows" "needs $MinDriftRows" 'SKIP'
                continue
            }
            $grown = (Get-Median $settled $drift.Property) - (Get-Median $baseline $drift.Property)
            $limit = "<= $(Format-Number ($drift.Max / $drift.Unit))$($drift.Suffix)"
            Add-Result $later $check "$(Format-Number ($grown / $drift.Unit) $drift.Format)$($drift.Suffix)" $limit (Get-Verdict ($grown -le $drift.Max))
        }
    }
}

$run = [pscustomobject]@{ Index = 'all'; Step = ''; Rows = $rows }

if (-not $hasDevices) {
    Add-Result $run 'devices constant' 'no devices column' '' 'SKIP'
}
else {
    $changes = [System.Collections.Generic.List[string]]::new()
    for ($i = 1; $i -lt $rows.Count; $i++) {
        if ($rows[$i].Devices -ne $rows[$i - 1].Devices) {
            $changes.Add("$($rows[$i].Utc.ToString('HH:mm:ss', $Invariant)) $($rows[$i - 1].Devices) -> $($rows[$i].Devices)")
        }
    }
    $value = if ($changes.Count -eq 0) { "$($rows[0].Devices)" } else { $changes -join '; ' }
    Add-Result $run 'devices constant' $value 'no change' (Get-Verdict ($changes.Count -eq 0))
}

if (-not $hasGpuInstances) {
    Add-Result $run 'gpu_instances churn' 'no gpu_instances column' '' 'SKIP'
}
else {
    $sampling = @($segments | Where-Object { $_.Rows[$_.Rows.Count - 1].Samples -gt $_.Rows[0].Samples })
    if ($sampling.Count -eq 0) {
        Add-Result $run 'gpu_instances churn' 'no sampling segment' '' 'SKIP'
    }
    else {
        $floor = $null
        foreach ($segment in $sampling) {
            $measure = $segment.Rows | Measure-Object GpuInstances -Minimum -Maximum
            $value = "$($measure.Minimum)/$($measure.Maximum)"
            if ($null -eq $floor) {
                $floor = $measure.Minimum
                Add-Result $segment 'gpu_instances min/max' $value 'floor reference' 'INFO'
            }
            else {
                $limit = $floor + $GpuTouchInstances
                Add-Result $segment 'gpu_instances min/max' $value "min <= $limit" (Get-Verdict ($measure.Minimum -le $limit))
            }
        }
        $all = $sampling | ForEach-Object { $_.Rows } | Measure-Object GpuInstances -Minimum -Maximum
        $range = $all.Maximum - $all.Minimum
        Add-Result $run 'gpu_instances churn' "range $range" ">= $GpuTouchInstances" (Get-Verdict ($range -ge $GpuTouchInstances))
    }
}

$results | Format-Table -AutoSize | Out-String -Width 4096 | Write-Output

# Least squares over the rows after the first segment, against minutes since the first of them.
$trend = @($rows | Select-Object -Skip $segments[0].Rows.Count)
$origin = if ($trend.Count -gt 0) { $trend[0].Utc } else { $null }
$minutes = @($trend | ForEach-Object { ($_.Utc - $origin).TotalMinutes })
$meanMinutes = ($minutes | Measure-Object -Average).Average
$spread = 0.0
foreach ($m in $minutes) { $spread += ($m - $meanMinutes) * ($m - $meanMinutes) }
if ($trend.Count -lt 2 -or $spread -eq 0) {
    "Whole-run trend after segment 1 (not scored): too few rows."
}
else {
    $slopes = foreach ($property in 'PrivateBytes', 'Handles') {
        $mean = ($trend | Measure-Object $property -Average).Average
        $sum = 0.0
        for ($i = 0; $i -lt $trend.Count; $i++) { $sum += ($minutes[$i] - $meanMinutes) * ($trend[$i].$property - $mean) }
        $sum / $spread
    }
    # The third section keeps a slope that rounds to zero from printing as -0.
    "Whole-run trend after segment 1 (not scored): private bytes $(Format-Number ($slopes[0] / 1KB) '0.0;-0.0;0.0') KB/min, " +
        "handles $(Format-Number $slopes[1] '0.00;-0.00;0.00')/min, over $($trend.Count) rows."
}

$failed = @($results | Where-Object Result -EQ 'FAIL').Count
$passed = @($results | Where-Object Result -EQ 'PASS').Count
$skipped = @($results | Where-Object Result -EQ 'SKIP').Count
"$(Split-Path -Leaf $Csv): $passed PASS, $failed FAIL, $skipped SKIP over $($segments.Count) segments, warm-up $WarmupMinutes min."
if ($failed -gt 0) { exit 1 }
exit 0
