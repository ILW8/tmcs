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
    Prints a table with one row per check and exits 0 when no check FAILs, otherwise 1.
.EXAMPLE
    pwsh -File tools/soak-report.ps1 -Csv artifacts/soak/m1-debug.csv
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Csv,

    [ValidateRange(0, 10080)]
    [double] $WarmupMinutes = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$MaxSwitchMs = 100
$MaxPrivateGrowth = 2MB
$MaxGuiRange = 5
$MaxHandlesRange = 50
$Invariant = [cultureinfo]::InvariantCulture

$rows = @(Import-Csv -LiteralPath $Csv | ForEach-Object {
    [pscustomobject]@{
        Utc          = [datetime]::Parse($_.utc, $Invariant, [System.Globalization.DateTimeStyles]::RoundtripKind)
        Step         = $_.step
        Samples      = [long]::Parse($_.samples, $Invariant)
        PrivateBytes = [long]::Parse($_.private_bytes, $Invariant)
        Handles      = [long]::Parse($_.handles, $Invariant)
        Gdi          = [long]::Parse($_.gdi, $Invariant)
        User         = [long]::Parse($_.user, $Invariant)
        SwitchMs     = [double]::Parse($_.switch_ms, $Invariant)
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

foreach ($segment in $segments) {
    if ($segment.Step -in 'done', 'none') {
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

$results | Format-Table -AutoSize | Out-String -Width 4096 | Write-Output

$failed = @($results | Where-Object Result -EQ 'FAIL').Count
$passed = @($results | Where-Object Result -EQ 'PASS').Count
$skipped = @($results | Where-Object Result -EQ 'SKIP').Count
"$(Split-Path -Leaf $Csv): $passed PASS, $failed FAIL, $skipped SKIP over $($segments.Count) segments, warm-up $WarmupMinutes min."
if ($failed -gt 0) { exit 1 }
exit 0
