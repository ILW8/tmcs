# Task Manager Details tab: the bottom band and End task button

Status: **measured** on 2026-10-26 at 12:33 local, from the owner's Task Manager (Taskmgr.exe 10.0.19041.4957, window at 96 DPI), with `tools/capture-taskmgr.ps1 -Details` run elevated. This replaces the unmeasured fallback of ruling R-Q (band 48, button 96x28, 10 below the band top, 16 from the right).

## Geometry (96-DPI units, scaled by `DeviceDpi / 96`)

From `details.txt` (window-relative physical pixels; at 96 DPI they equal 96-DPI units):

| Element (accessibility tree) | Rect (x, y, w, h) |
|---|---|
| pane "Command Bar" | 8, 656, 1278, 48 |
| push button "Fewer details" | 16, 666, 96, 28 |
| push button "End task" | 1193, 668, 85, 23 |

| Element | Value |
|---|---|
| Bottom band | 48 high, full width of the view (the Command Bar pane spans the tab content), white, no line at its top. The list fills the space above it. |
| End task button | 85 x 23 |
| Button top | 12 below the band's top (668 - 656) |
| Button right edge | 8 from the band's right edge (8 + 1278 - (1193 + 85)) |
| Text | `&End task` (string 38001) |
| Enabled | only while at least one row is selected |

The band's left holds Task Manager's "Fewer details" button (96 x 28, 16 from the left, 10 below the band top), the same as on the Performance tab. tmcs leaves that space empty on every tab, as `PerformanceView` does.

In `DetailsView`, `LayoutBand(dpi)` applies these, each value rounded after scaling.

## To capture again

1. Open Task Manager on any tab (not minimized; it may sit behind other windows).
2. Run the capture elevated, from the repo root, in PowerShell:

   ```powershell
   Start-Process pwsh -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile', '-File', "$PWD\tools\capture-taskmgr.ps1", '-OutDir', "$PWD\artifacts\reference\taskmgr", '-Details'
   ```

   It selects the Details tab, saves `details.png` and `details.txt` (the accessibility tree, each element's bounds relative to the window) and `log.txt` into `artifacts/reference/taskmgr/`, logs the End task button's rectangle, and selects the original tab again. It never moves the mouse or takes focus.
3. Read the band and button rectangles from `details.txt`, convert from physical pixels to 96-DPI units (divide by the window DPI in `log.txt` / 96), and update the tables above and the `BandHeight`, `ButtonWidth`, `ButtonHeight`, `ButtonTop` and `ButtonRightMargin` constants in `DetailsView`.

The captures show Microsoft's UI: reference only, never commit them (`artifacts/` is git-ignored).
