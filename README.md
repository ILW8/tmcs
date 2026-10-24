# tmcs

tmcs is a small, original Windows 10 Task Manager replacement written in C# (WinForms, .NET 10). It exists because the stock Task Manager leaks icons and handles when left running for days; tmcs lists processes in a virtual-mode Details view and releases per-process state, including icons, on the same tick a process exits.

## Build and test

```
dotnet build
dotnet test
```

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

- `TMCS_DIAG` is the path of the CSV to write. Setting it turns test mode on.
- `TMCS_SCENARIO` is optional and is read only in test mode. It is a list of `action:minutes` steps separated by commas, for example `details:10,minimized:30,details:3,paused:5,details:2`. The actions are `details` (restored, Details tab, sampling), `minimized` (minimized, sampling) and `paused` (restored, paused). Minutes may be fractional.

Set them for the tmcs process only. tmcs removes both from its own environment when it starts, so the processes it starts do not inherit them.

In test mode tmcs:

- skips the single-instance check, so it can run beside a normal instance;
- shows its window without activating it and keeps it at the bottom of the z-order, so it never takes focus or rises above your windows;
- starts at Normal speed, not on top and not maximized, whatever the settings file says;
- reads the settings file but never saves it;
- writes one CSV row every 10 seconds (private bytes, handles, GDI and USER objects, icon and row counts, CPU time, samples taken, and how long the last scenario step took to apply), replacing any file already at that path;
- plays the scenario, if there is one, and exits with code 0 when it ends. Without a scenario it logs until you close the window.

A scenario that does not parse is reported before any window appears: tmcs writes the parser's message to `<TMCS_DIAG>.error.txt` and exits with code 2.

### Non-elevated soak (Debug build)

Start the churn in one terminal. It runs for the given minutes, then prints how many processes it started:

```
dotnet build -c Debug
pwsh -File tools/churn.ps1 -Parallel 8 -Minutes 52
```

In another, run tmcs with the variables set for that process only. Piping to `Out-Null` makes the command wait until tmcs exits (this scenario takes 50 minutes):

```
pwsh -NoProfile -Command '$env:TMCS_DIAG = "$PWD/artifacts/soak/m1-debug.csv"; $env:TMCS_SCENARIO = "details:10,minimized:30,details:3,paused:5,details:2"; & ./src/Tmcs/bin/Debug/net10.0-windows/win-x64/tmcs.exe | Out-Null'
```

Then check the log:

```
pwsh -File tools/soak-report.ps1 -Csv artifacts/soak/m1-debug.csv
```

### Elevated soak (published build)

Run the published exe the same way, with churn running (keep it going for the whole scenario, here 30 minutes, so `-Minutes 32`). A UAC-elevated process does not inherit your environment variables, so the wrapper starts an elevated `pwsh` that sets them itself and then starts tmcs. This shows one UAC prompt:

```
Start-Process pwsh -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile', '-Command', "`$env:TMCS_DIAG='$PWD\artifacts\soak\m1-elevated.csv'; `$env:TMCS_SCENARIO='details:10,minimized:10,details:3,paused:5,details:2'; & '$PWD\artifacts\publish\tmcs.exe' | Out-Null"
pwsh -File tools/soak-report.ps1 -Csv artifacts/soak/m1-elevated.csv
```

Run the report once tmcs has exited.

### Reading the report

`soak-report.ps1` splits the CSV into segments of consecutive rows with the same step and prints one row per check: PASS, FAIL or SKIP. It exits with code 0 when no check fails and 1 otherwise.

For every segment, switching into it must take at most 100 ms, which also catches a stall after restoring from minimized. A `paused` segment must take no samples. After a 2-minute warm-up (`-WarmupMinutes`), private bytes may grow by at most 2 MB, GDI and USER objects may vary by at most 5, and handles by at most 50. A segment too short to have two rows after warm-up reports SKIP for those checks.

The design is in [docs/superpowers/specs/2026-10-23-tmcs-design.md](docs/superpowers/specs/2026-10-23-tmcs-design.md).
