# tmcs

tmcs is a small, original Windows 10 Task Manager replacement written in C# (WinForms, .NET 10). It exists because the stock Task Manager leaks icons and handles when left running for days; tmcs lists processes in a virtual-mode Details view and releases per-process state, including icons, on the same tick a process exits.

## Build and test

```
dotnet build
dotnet test
```

While a Debug tmcs is running it locks `src\Tmcs\bin\Debug`, so build and test into another folder with `dotnet test --artifacts-path artifacts/verify`. Keep that folder inside the repository: some tests find the repository from their output folder.

Debug builds run as the invoking user (no UAC prompt). Release builds, and so the published exe, request administrator rights, so launching one shows a UAC prompt.

## Publish

```
dotnet publish src/Tmcs -p:PublishProfile=win-x64
```

The profile is `src/Tmcs/Properties/PublishProfiles/win-x64.pubxml`: Release, win-x64, self-contained, single file. The output is `artifacts/publish/tmcs.exe`, with a `tmcs.pdb` beside it and no other files. The exe needs no installed .NET runtime. `artifacts/` is git-ignored.

Put `tmcs.exe` in a folder only administrators can write to, for example `C:\Program Files\tmcs\`: it runs elevated, and Windows loads some DLLs from the exe's own folder.

### Sign

Sign the exe with your own certificate:

```
signtool sign /fd SHA256 /a /tr <timestamp-url> /td SHA256 artifacts\publish\tmcs.exe
```

`/a` picks the best certificate in your store; `<timestamp-url>` is your certificate authority's RFC 3161 timestamp server.

## Soak test

The soak checks that tmcs does not leak while processes start and exit continuously. It has three parts: `tools/churn.ps1` starts and ends short-lived processes, tmcs in test mode logs its own counters to a CSV, and `tools/soak-report.ps1` checks the CSV. Run every command from the repository root in `pwsh`.

### Test mode

Test mode is switched on by environment variables, not command-line flags (tmcs ignores its arguments):

- `TMCS_DIAG` is the path of the CSV to write, and must end in `.csv`. Setting it turns test mode on.
- `TMCS_SCENARIO` is optional and is used only in test mode. It is a list of `action:minutes` steps separated by commas, for example `details:10,minimized:30,cpu:3,paused:5,details:2`. Minutes may be fractional. Each action sets the whole state, whatever the step before left:
  - `details`: restored, on the Details tab, sampling.
  - `cpu`: restored, on the Performance tab's CPU pane, sampling.
  - `network`: restored, on the pane of the first network adapter the sidebar lists (never a hidden one), sampling.
  - `gpu`: restored, on GPU 0's pane, sampling.
  - `minimized`: minimized, sampling.
  - `paused`: restored, on the CPU pane, paused.

  A `network` or `gpu` step on a PC whose sidebar lists no such device ends the run with code 5.

Set them for the tmcs process only. tmcs removes both from its own environment when it starts, so the processes it starts do not inherit them.

In test mode tmcs:

- skips the single-instance check, so it can run beside a normal instance;
- shows its window without activating it and keeps it at the bottom of the z-order, so it never takes focus or rises above your windows;
- starts at Normal speed, not on top and not maximized, whatever the settings file says;
- reads the settings file but never saves it;
- writes one CSV row every 10 seconds (private bytes, handles, GDI and USER objects, icon and row counts, CPU time, samples taken, and how long the last scenario step took to apply), replacing a CSV that tmcs wrote before at that path (it refuses any other file already there, see below);
- plays the scenario, if there is one, and exits with code 0 when it ends. Without a scenario it logs until you close the window.

Nobody watches a test run, so test mode never shows an error dialog. It writes the error to stderr and, except for code 3, to `<TMCS_DIAG>.error.txt` when that file can be written. The exit codes:

| Code | Meaning |
|---|---|
| 0 | The scenario ended, or you closed the window. |
| 2 | `TMCS_SCENARIO` does not parse. Reported before any window appears. |
| 3 | `TMCS_DIAG` is refused, before any window appears: it does not end in `.csv`, or it names an existing file whose first line is not the diagnostics header. tmcs writes nothing at all, not even the error file. |
| 4 | The CSV cannot be read, created or written, for example because another program has it locked or the folder is read-only. |
| 5 | An unhandled exception, for example in a timer tick, ended the run. |

### Non-elevated soak (Debug build)

Start the churn in one terminal. It runs for the given minutes, then prints how many processes it started. `-GpuParallel 2` also keeps two `gpu-touch` processes running; each creates a Direct3D 11 device for half a second, so the GPU's per-process counter instances come and go too. Build it first. One churn serves both soaks below: its 95 minutes cover the 78-minute scenario of each, with time to accept the elevated soak's UAC prompt.

```
dotnet build -c Debug
dotnet build tools/gpu-touch -c Release
pwsh -File tools/churn.ps1 -Parallel 8 -GpuParallel 2 -Minutes 95
```

In another, run tmcs with the variables set for that process only. Piping to `Out-Null` makes the command wait until tmcs exits (this scenario takes 78 minutes):

```
pwsh -NoProfile -Command '$env:TMCS_DIAG = "$PWD/artifacts/soak/m2-debug.csv"; $env:TMCS_SCENARIO = "details:1,gpu:1,minimized:1,network:12,cpu:12,gpu:12,details:12,minimized:12,cpu:3,paused:10,cpu:2"; & ./src/Tmcs/bin/Debug/net10.0-windows/win-x64/tmcs.exe | Out-Null'
```

Then check the log:

```
pwsh -File tools/soak-report.ps1 -Csv artifacts/soak/m2-debug.csv
```

### Elevated soak (published build)

Run the published exe the same way, while the churn runs. Start it within about 15 minutes of the churn, so the churn lasts the whole scenario.

A UAC-elevated process does not inherit your environment variables, so the wrapper starts an elevated `pwsh` that sets them itself and then starts tmcs. This shows one UAC prompt:

```
Start-Process pwsh -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile', '-Command', "`$env:TMCS_DIAG='$PWD\artifacts\soak\m2-elevated.csv'; `$env:TMCS_SCENARIO='details:1,gpu:1,minimized:1,network:12,cpu:12,gpu:12,details:12,minimized:12,cpu:3,paused:10,cpu:2'; & '$PWD\artifacts\publish\tmcs.exe' | Out-Null"
pwsh -File tools/soak-report.ps1 -Csv artifacts/soak/m2-elevated.csv
```

`-Wait` holds the prompt until the elevated `pwsh`, and so tmcs, has exited, so the report reads the finished log.

### Reading the report

`soak-report.ps1` splits the CSV into segments of consecutive rows with the same step and prints one row per check: PASS, FAIL or SKIP, or INFO for a value that is not a check. It exits with code 0 when no check fails and 1 otherwise.

For every scenario segment (rows with the step `none` or `done` are skipped), switching into it must take at most 100 ms, which also catches a stall after restoring from minimized. A `paused` segment must take no samples. After a 2-minute warm-up (`-WarmupMinutes`), private bytes may grow by at most 2 MB, GDI and USER objects may vary by at most 5, and handles by at most 50. A segment too short to have two rows after warm-up reports SKIP for those checks.

With the default 2-minute warm-up, the segment that holds minute 12 or so can FAIL the private-bytes check: the GC commits its heap over about the first 12 minutes, as [docs/superpowers/results/2026-10-23-m1-soak.md](docs/superpowers/results/2026-10-23-m1-soak.md) records. Judge leaks by the later segments and the drift rows below.

A slow leak passes every segment on its own, so a state that occurs in more than one segment is also checked for drift. Each later segment of the state is compared with the first by median: private bytes may be at most 1 MB higher, handles at most 8, and GDI and USER objects at most 5. The first segment counts the rows in its last half, and never a row before the warm-up. A later segment counts its rows from `-SettleSeconds` on (default 30). With fewer than `-MinDriftRows` rows (default 6, one minute) on either side, the comparison reports SKIP.

A heap commit still growing when the first segment's baseline starts shows as drift, and so do the window and painting objects a view makes the first time it is shown. So the first segment of a repeated state must start after the GC's heap commit and after every view has been shown once. The M2 scenario starts with a three-minute tour, `details:1,gpu:1,minimized:1`, then `network:12`, so its first `cpu` segment starts at minute 15. The tour's own segments are too short to score and report SKIP. The M1 scenarios repeat `details` from minute 0, so their drift rows FAIL by the size of that commit, about 4.9 MB.

Below the table, the report prints the least-squares slopes of private bytes (KB per minute) and handles (per minute) over every row after segment 1. They are for the record and are not scored.

When the CSV has the `devices` column, the number of devices must stay the same over the whole run. The check lists each change with its time, since every change must be explained.

When the CSV has the `gpu_instances` column, the report lists its minimum and maximum in each sampling segment, one whose `samples` advance. Leaked PDH instances raise the floor, so the minimum of each later sampling segment may be at most `-GpuTouchInstances` (default 13, the instances one gpu-touch process adds) above the first one's, which is an INFO row. Over all sampling rows, the range must be at least `-GpuTouchInstances`, which shows that instances come and go. Without these columns, as in the M1 logs, their checks report SKIP.

The design is in [docs/superpowers/specs/2026-10-23-tmcs-design.md](docs/superpowers/specs/2026-10-23-tmcs-design.md).
