# Task Manager Details tab: the bottom band and End task button

Status: **fallback geometry, NOT measured.** The M3 run was unattended, so the capture of the owner's Task Manager (which needs one elevated run and so a UAC prompt) was deferred. tmcs lays out the Details tab with the values below until the owner runs the capture and refines them.

## Fallback geometry (96-DPI units, scaled by `DeviceDpi / 96`)

Controller ruling R-Q. The look reference's command-bar band on the Performance tab (`2026-10-24-taskmgr-performance-look.md`: band 48 high, "Fewer details" button 96x28, 16 from the left, 10 below the band top) is the precedent; the Details tab's button is placed the same way, at the right.

| Element | Value |
|---|---|
| Bottom band | 48 high, full width of the view, white, no line at its top. The list fills the space above it. |
| End task button | 96 x 28 |
| Button top | 10 below the band's top |
| Button right edge | 16 from the view's right edge |
| Text | `&End task` (string 38001) |
| Enabled | only while at least one row is selected |

In `DetailsView`, `LayoutBand(dpi)` applies these, each value rounded after scaling.

## To refine it (owner, later)

1. Open Task Manager on any tab (not minimized; it may sit behind other windows).
2. Run the capture elevated, from the repo root, with `-Details`:

   ```powershell
   Start-Process pwsh -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile', '-File', "$PWD\tools\capture-taskmgr.ps1", '-OutDir', "$PWD\artifacts\reference\taskmgr", '-Details'
   ```

   It selects the Details tab, saves `details.png` and `details.txt` (the accessibility tree, each element's bounds relative to the window) and `log.txt` into `artifacts/reference/taskmgr/`, logs the End task button's rectangle, and selects the original tab again. It never moves the mouse or takes focus.
3. Read the band and button rectangles from `details.txt` (the End task push button, and the pane that holds it), convert from physical pixels to 96-DPI units (divide by the window DPI in `log.txt` / 96), and replace the table above and the `BandHeight`, `ButtonWidth`, `ButtonHeight`, `ButtonTop` and `ButtonRightMargin` constants in `DetailsView`.

The captures show Microsoft's UI: reference only, never commit them (`artifacts/` is git-ignored).
