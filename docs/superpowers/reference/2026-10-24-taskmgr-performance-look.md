# Task Manager Performance tab: measured look

Measured on 2026-10-24 from `artifacts/reference/taskmgr/00`–`20` (Taskmgr.exe 10.0.19041.4957, PrintWindow, window 1294×712, 96 DPI, 100% scale) and their MSAA dumps. It holds everything needed to redraw the tab 1:1 in tmcs (spec 4.3).

- **Coordinates:** window pixels, PNG (0,0) = window top-left. "rect x,y,w,h"; "a–b" is an inclusive pixel range.
- **Dumps vs images:** each dump was taken a moment after its PNG, so some values differ. For example, 01's dump says "10%" where the image shows "14%". Positions that depend on a value's width are taken from the pixels, not the dump.
- **Colors:** exact pixel values, taken from the most common value or from runs of identical pixels. Each section says where it sampled.
- **Fonts:** matched pixel for pixel against offscreen GDI renders. The renders used `CreateFont(-px, FW_NORMAL, CLEARTYPE_QUALITY, "Segoe UI")` and `TextOut` into a memory DIB, with no window or screen involved. Every style below matched with a mean difference of 0.0. The Light, Semilight and Semibold faces and the neighbouring sizes all differed by 15–60.

## 1. Tab layout

| Item | Rect / range | Notes |
|---|---|---|
| Window client | 8,51,1278,653 | x 8–1285, y 51–703 |
| Tab strip | 8,53,1278,28 | Its bottom line is D9D9D9 at y=75. Rows 76–80 are white. |
| Tab page content | from y=81 | Both panes start at y=81. |
| Left sliver | x=8, y 81–655 | F0F0F0, 1 px. The Charts pane starts at x=9. |
| **Charts** (sidebar) pane | 9,81,225,575 (x 9–233, y 81–655) | White. Entries start at (12,88). Entries are clipped at x=208. |
| Sidebar scrollbar | dump 209,88,17,568; painted x 210–225, y 88–655 | x=209 is white. Details below. |
| Gap between panes | x 226–233 | White, with no separator. The exception is a 1-px D9D9D9 vertical line at x=226, y 98–651, present in every capture. |
| **Tables** (right) pane | 234,81,1052,575 (x 234–1285, y 81–655) | White. |
| Command bar band | 8,656,1278,48 (y 656–703) | White, with no line at its top. It holds "Fewer details" (button 16,666,96,28), the icon (127,671,16,16) and the link (144,672,134,15). tmcs keeps the empty band. |

**Scrollbars** (standard Windows 10 themed look):

- **Sidebar scrollbar:**
  - Track F0F0F0.
  - Arrow buttons are 17 px tall: y 88–104 and 639–655.
  - The arrow glyph is a 606060 chevron, 7 wide and 6 tall, at x 214–220. Up: y 94–99; down: y 645–650. Bitmap of the up arrow (`#` = 606060):
    ```
    ...#...
    ..###..
    .#####.
    ###.###
    ##...##
    #.....#
    ```
  - Thumb CDCDCD, x 210–224 (x=225 stays F0F0F0), y 105–336, 232 px. The view is 568 px, the content 1308 px (y 88–1396), and the track 534 px: thumb = track × view / content.
- **GPU pane scrollbar** (appears only when the pane's content overflows):
  - Dump rect 1269,81,17,575. Painted x 1270–1285, y 81–655.
  - Thumb x 1270–1284, y 98–603: 506 of a 541-px track. The content is 615 px tall in a 575-px view.
  - It has no D9D9D9 line. With it present, the pane content's right edge moves from 1262 to 1245.

## 2. Sidebar entries

| | CPU, Memory | Disk N | Network, Wi-Fi | GPU 0 |
|---|---|---|---|---|
| Entry rect | x=12, 194×60 | 194×66 | **204**×66 (clipped at x=208) | 204×66 |
| Mini graph | entry + (10,10), 60×40 | 60×46 | 60×46 | 60×46 |
| Text column | x=92 (entry.x+80) | same | 92–207 (115 px); overflow gets "..." (three periods) | same |
| Lines | title, sub 1 | title, sub 1, sub 2 | title, sub 1, sub 2 | title, sub 1, sub 2 |

- **Stacking:** entries are stacked with no gaps from y=88: CPU 88, Memory 148, Disk 0 208, Disk 1 274, Disk 2 340, Disk 3 406, Disk 4 472, then the 12 adapters from 538 at 66 each, and GPU 0 at 1330.
- **Mini graph height:** entry height − 20.
- **Title:** origin (92, entry.y+5). Segoe UI 16 px, black 000000. Box 21 tall.
- **Sub line 1:** origin (93, entry.y+26). Segoe UI 12 px, 000000. Box 15 tall. Sub lines are drawn 1 px right of their rect.
- **Sub line 2:** origin (93, entry.y+41).
- **Line formats** seen:

| Entry | Sub 1 | Sub 2 |
|---|---|---|
| CPU | `14%` then `4.43 GHz` | none |
| Memory | `38.4/63.7 GB (60%)` | none |
| Disk | `SSD` | `0%` |
| Ethernet / Wi-Fi | connection name: `Tailscale`, `vEthernet (Default S...` | `S: ` `0 ` `R: ` `0 Kbps ` |
| GPU 0 (offscreen, dump only) | `NVIDIA GeForce RTX 3090` | `14%` then `(38 °C)` |

- **Two-part lines** (CPU, GPU): the first text starts at x=93, the second 5 px after the first text's end. For example, "14%" is 22 px wide, so "4.43 GHz" starts at 93+22+5 = 120, and after "8%" it starts at 114. The dump rects are first = text width+1 and second = text width+5. For GPU 0 the rects are 92 w23 and 115 w41.
- **Network line:**
  - Four segments, each a text with a trailing space, placed one after another. Each rect is text width+1.
  - `S: ` and `R: ` are 707070; the values are 000000.
  - The unit appears only after the receive value: `S: 8.0 R: 40.0 Kbps`. With send "8.0 ", "R:" moves from x=115 to 124.
- **Selected entry:** CDE8FF fill over the whole entry rect (CPU x 12–205, y 88–147; Disk 4 12–205 × 472–537; a network entry 12–208).
  - It has no border and no focus rectangle, and the text colors don't change. The mini graph's interior stays white.
  - Unselected entries are white.
  - The hover look was not captured.
- **Mini graphs:**
  - 1-px border in the device color. The corner pixels are lighter (table in section 3).
  - White interior, **no grid**. The sample spacing is exactly 1 px: the border-to-border span is 59 and there are 60 samples, the same rule as section 4.
  - CPU: the total-utilization line 117DBB and fill F1F6FA only, with no kernel series (no D9E7F1 pixels).
  - Memory: line 9528B4 and fill F4F2F4.
  - Disk: the active-time line 4DA60C with its fill.
  - Network: a receive line (A74F01) and a send line (orange, around DF672C). At this size it can't be told whether the send line is dashed or filled.
- **Not visible:** the Wi-Fi and GPU 0 entries are below the visible sidebar in every capture, so their pixels, including the GPU mini graph and the temperature text color, are unknown. Only their dump rects exist.

## 3. Graph colors

How these were identified:

- **Border:** the most common color on the chart rect's outer ring.
- **Grid:** the most common non-white interior color in empty areas (for example the empty Disk 0–3 and vEthernet graphs).
- **Fill:** the most common color under the line.
- **Grid in fill:** the color where a grid line crosses the filled area.
- **Lines:** the color of horizontal or vertical runs of the line. For the CPU kernel line, 81B3D2 is the only color forming runs between the two fills across all 32 graphs.

| Device | Border | Grid | Main line | Fill under main | Grid in fill | Other series |
|---|---|---|---|---|---|---|
| CPU (per LP) | 117DBB | D9EAF4 | 117DBB, total utilization | F1F6FA | CEE2F0 | **Kernel:** line 81B3D2, fill D9E7F1 (from the kernel line down), grid in that fill BAD6E8 |
| Memory | 8B12AE | ECDEF0 | 9528B4 | F4F2F4 | E3D4E6 | none |
| Disk active time | 4DA60C | DBEDCE | 4DA60C | EFF7E9 | CEE7BC | none |
| Disk transfer rate | 4DA60C | DBEDCE | **Read**: 4DA60C, solid | EFF7E9 | CEE7BC | **Write:** 79D50D, dashed, no fill. Scale-marker line A4DE9A. |
| Network / Wi-Fi | A74F01 | EEDECF | **Receive**: A74F01, solid | FCF3EB | EDD5C0 | **Send:** DF672C, dashed, no fill. Scale-marker line D2A883. |
| GPU engines + memory | 117DBB | D9EAF4 | 117DBB | F1F6FA | CEE2F0 | none |

- **Border corners:** every graph border, the mini graphs included, sits on the chart rect's outer pixels, 1 px wide. Its four corner pixels are lighter:

  | Device | Top corners | Bottom corners |
  |---|---|---|
  | CPU / GPU | 3A94C7 | 1F85BF |
  | Memory | 9F3BBB | 9220B3 |
  | Disk | 6CB536 | 58AC1A |
  | Network | B66D2D | AD5A10 |

- **Fill over the border:** where the memory fill covers the left border column, that column shows 9528B4.
- **Lines:**
  - All data lines are 1 px and anti-aliased, with a GDI+ AntiAlias look.
  - Each point is rounded to a whole pixel before the line and fill are drawn, so a steady value is one row in the line's own color. Capture 02's memory line is 9528B4 on row 241, then on row 240. A GDI+ render with rounded points matches its step at x 1086–1111 to a total channel difference of 69. With x not rounded, the difference is 239. With y 0.4 px below a whole pixel, it is 8,120. (Found in the owner's fidelity check on 2026-10-25.)
  - The dashed series use the GDI+ `DashStyle.Dash` pattern, 3 on and 1 off. On a vertical stretch of the send line (capture 10, x=562) the pixels alternate 2× DF672C and 2× EFB395 (50%), which is a 3:1 dash at a half-pixel phase.
- **Grid alpha:** the Disk grid equals 4DA60C at 20% over white exactly. The CPU grid is about 117DBB at 16%, and that drawn over F1F6FA gives the measured CEE2F0 to within 1. The other devices don't fit a single alpha, so use the opaque values from the table.
- **Memory composition bar** (02):
  - Caption "Memory composition", 11 px, at (253,397).
  - Outer border 8B12AE, x 253–1261, y 415–466, with square corners.
  - The segments fill y 416–465. Every segment after the first starts with a 1-px CEB0D7 line.

  | Segment | x | Fill |
  |---|---|---|
  | In use | 254–856 | F4F2F4 (same as the graph fill) |
  | Modified | 857–861 | CEB0D7 line at 857, then EEE6F1 |
  | Standby | 862–1215 | CEB0D7 line, then FFFFFF |
  | Free | 1216–1260 | CEB0D7 line, then FFFFFF |

  - The dump's widths for In use, Modified, Standby and Free are 603, 5, 354 and 45, against their tooltip amounts of 38950, 297, 22875 and 3058 MB. The exact rounding rule could not be determined.

## 4. Graph geometry

**Rules common to every graph:**

- Let `top`, `bottom`, `left` and `right` be the border pixel rows and columns: `bottom = y+h−1`, `right = x+w−1`.
- **Rows:** there are always 10. Horizontal grid lines sit at `y = ceil(top + k·(bottom−top)/10)` for k = 1..9. This was checked on the Disk, Network, CPU and GPU graphs.
- **Samples:** 60 points span the graph from border to border, `dx = (right−left)/59`, with the newest at `right`. In capture 10 the line's vertices sit exactly at these x positions.
- **Vertical grid lines:** they sit on sample positions, one every `s` samples: `x = round(right − (p + s·j)·dx)` for j = 0, 1, 2, …
  - `p` (0..s−1) is the scroll phase.
  - **tmcs differs on purpose (owner's request, 2026-10-26):** the lines sit on every `s`-th *timer* sample, so they stay `s` intervals apart in time. A refresh (F5) between ticks starts no line. While paused, a refresh counts once a sampling interval has passed since the last counted sample. At a steady speed this is every `s`-th sample, as above.
  - In capture 10 the vertices at x=1158 and 1210 lie on grid lines.

| Pane | Graph | Rect | dx | s | Column spacing | Row spacing | Phase p in capture |
|---|---|---|---|---|---|---|---|
| CPU | each logical processor | 122×76 (the last column is 123×76) | 2.051 | 10 | 20.5 | 7.5 | 9 |
| Memory | Memory usage | 253,150,1009,226 | 17.085 | 3 | 51.25 | 22.5 | 2 |
| Disk | Active time | 253,150,1009,263 | 17.085 | 3 | 51.25 | 26.2 | see below |
| Disk | Disk transfer rate | 253,450,1009,67 | 17.085 | 3 | 51.25 | 6.6 | same as Active time |
| Ethernet | Throughput | 253,150,1009,367 | 17.085 | 3 | 51.25 | 36.6 | see below |
| Wi-Fi | Throughput | 253,150,1009,352 | 17.085 | 3 | 51.25 | 35.1 | 1 |
| GPU | 3D, Copy | 257,158,488,73 and 753,158,488,73 | 8.254 | 4 | 33.0 | 7.2 | 3 |
| GPU | Video Encode, Video Decode | 257,259,488,74 and 753,259,488,74 | 8.254 | 4 | 33.0 | 7.3 | 3 |
| GPU | Dedicated / Shared GPU memory usage | 255,360,988,67 and 255,447,988,67 | 16.729 | 3 | 50.2 | 6.6 | 1 |

**Grid movement across captures (1009-wide graphs).** The first line sits at x=287 (p=0), 270 (p=1) or 304 (p=2):

| Captures | p |
|---|---|
| 03, 04, 05, 06 | 2 |
| 07 | 0 |
| 08 | 2 |
| 09 | 1 |
| 10 | 0 |
| 11, 12 | 1 |
| 13 | 0 |
| 14, 15, 16, 17 | 1 |
| 18 | 0 |
| 19 | 1 |

- **The data scrolls left at 1 sample/s (measured).**
  - The CPU mini graph, where dx is 1 px, shows the same history in every capture.
  - Between consecutive captures its profile matches exactly after a **left** shift of 2–3 samples (2.3–2.65 s apart) or 4–5 samples (4.4–4.8 s apart).
  - In a 1009-wide graph this is 17.1 px/s leftward.
- **The grid lines move in whole-sample steps (dx = 17.1 px)**: the phase differs between captures of the same pane type.
- But the phase changes **don't follow the number of samples that elapsed**. For example, 05→06 is 2 samples but p stays 2, and 06→07 is 2 samples with p going 2→0. No fixed period fits the `log.txt` times either; the best fits 13 of 17.
- So the grid phase is per-graph state, not a global sample count. Whether a visible graph's grid advances one dx per sample, scrolling with the data, **can't be confirmed**: each pane was captured only once. Scrolling the grid with the data reproduces every capture.
- How `s` is chosen for other graph widths can't be determined, because there is only one window size.

**Captions** (Segoe UI 11 px, 707070, box 13 tall):

| Caption | Position |
|---|---|
| Top left | (253,134); CPU (255,134) |
| Top right (scale) | y=134, right-aligned so its right edge is the graph's right edge `x+w` (1262; CPU 1260) |
| Bottom left `60 seconds` | (253, y+h+2) |
| Bottom right `0` | right edge at `x+w`, same y |

| Pane | Top left / top right | Bottom |
|---|---|---|
| CPU | `% Utilization over 60 seconds` / `100%` | Empty 13-px row at (255,468); no "60 seconds" |
| Memory | `Memory usage` / `63.7 GB` | `60 seconds` / `0` at y=378 |
| Disk | `Active time` / `100%` | at 415 |
| Disk | `Disk transfer rate` / `10 MB/s` (y=434) | at 519 |
| Network | `Throughput` / `100 Kbps` or `500 Kbps` | at 519 (Wi-Fi 504) |
| GPU | `Dedicated GPU memory usage` / `24.0 GB` at (255,344); `Shared GPU memory usage` / `31.8 GB` at (255,431) | none; the right edge is 1243 |

**Inner scale marker** (Disk transfer rate in 07; Throughput in 10):

- **Line:** a 1-px horizontal line across the interior, x left+1 to right−1.
  - Disk: A4DE9A (end pixels BBE7B4) at y=469, for 7 of 10 MB/s.
  - Network: D2A883 (first pixel DEBEA2) at y=186, for 450 of 500 Kbps.
  - Both fit `y = floor(bottom − v/max·(bottom−top))`.
- **Label:** `7 MB/s` / `450 Kbps`, 11 px, 707070, right-aligned so the text box's right edge is at `right − 4` (1257). The text box top is the line's y − 16, so the box ends 3 px above the line. It is drawn transparently: grid lines show between the glyphs.
- Which value the marker shows can't be determined from the captures. It isn't the visible maximum: the Disk 4 write peak is about 3.6 MB/s.

**CPU per-LP grid:**

- 8 columns × 4 rows in row-major order: LP 0 top left, LP 7 top right, LP 31 bottom right (dump names `CPU00`…`CPU310`).
- Column x: 255, 381, 507, 633, 759, 885, 1011, 1137. The pitch is 126 with 4-px gaps; widths are 122, except the last column is 123 and ends at 1260.
- Row y: 150, 230, 310, 390. The pitch is 80; height 76 with 4-px gaps.
- The area spans x 255–1260, y 150–465 (pane "Logical CPU chart pane" at 253,134,1009,347).

**GPU pane:**

- **Engine cells, 2×2:**
  - The label buttons are at (255,136), (751,136), (255,237) and (751,237), each 17 tall.
  - Each graph sits at label.x+2 and starts 5 px below its label row: row 1 at 158–230, row 2 (label at 231+6) at 259–332. Each graph is 488 wide, with an 8-px gap between the two columns.
  - The engine utilization (for example `13%`) is 11 px, 707070, at y = label y + 2, right-aligned to graph right + 2 (747 / 1243).
- **Memory graphs:** each caption sits 16 px above its graph. The second caption is 4 px below the first graph.
- **Chart area:** y 134–520 (dump grouping 251,134,994,387), then the info block from 531.
- **Overflow:** the content overflows (see section 7), hence the scrollbar.

## 5. Text styles

All text is **Segoe UI Regular** rendered by GDI ClearType (colored fringes, so WinForms `TextRenderer`). pt = px × 0.75 at 96 DPI. The box height is the GDI line height for that size.

| Style | px | pt | Color | Box h | Where |
|---|---|---|---|---|---|
| Pane title (`CPU`, `Memory`, `Disk 4 (C:)`, `Ethernet`, `Wi-Fi`, `GPU`) | 28 | 21 | 000000 | 38 | origin (251,91) |
| Pane subtitle (device name) | 16 | 12 | 000000 | 21 | right-aligned to 1262 (GPU 1245), origin y=105 |
| Graph captions, scale labels, marker labels, GPU engine % | 11 | 8.25 | 707070 | 13 | section 4 |
| Labels above big values (`Utilization`, `Speed`, …) | 12 | 9 | 707070 | 15 | section 7 |
| Big values (`14%`, `4.43 GHz`, `25:17:28:40`) | 20 | 15 | 000000 | 28 | 15 px below their label |
| Static table keys (`Base speed:`) | 12 | 9 | 707070 | 15, rows 18 apart | section 7 |
| Static table values (`4.50 GHz`) | 12 | 9 | 000000 | 15 | section 7 |
| Sidebar title | 16 | 12 | 000000 | 21 | section 2 |
| Sidebar sub lines | 12 | 9 | 000000; `S:` and `R:` 707070 | 15 | section 2 |
| GPU engine names (`3D`, `Copy`, `Video Encode`, `Video Decode`) | 13 | 9.75 | 000000 | 17 | text at button.x+15, button.y |

- **Subtitles:**
  - Memory shows the installed total (`64.0 GB`).
  - Disk shows the model (`Sabrent Rocket Q`).
  - Network shows the adapter description (`HP NC550SFP Dual Port 10GbE Server Adapter #2`), while the sidebar shows the connection name.
  - The CPU string carries a trailing space (`AMD Ryzen 9 7950X 16-Core Processor `), so its ink ends 4 px before 1262.
- **Text origin:** text is drawn at its dump rect's top left (TextOut origin). The exceptions are static-table text and sidebar sub lines, which sit 2 px and 1 px right of their rects.

## 6. Pane-specific pieces

**Legend markers** sit left of the Read/Write and Send/Receive values. Each is 2 px wide and starts 3 px below its label's top:

| Pane | Marker | x | y | Look |
|---|---|---|---|---|
| Disk | Read speed | 253–254 | 596–635 (40 px) | solid 4DA60C |
| Disk | Write speed | 341–342 | 596–634 | 79D50D dashes, 3 on 3 off: 7 dashes, 596–598 … 632–634 |
| Network | Send | 253–254 | 545–583 (Wi-Fi 530–568) | DF672C dashes, 3 on 3 off, 7 dashes |
| Network | Receive | 253–254 | 596–635 (Wi-Fi 581–620) | solid A74F01 |

- **Dump rects:** each marker is a 2×43 "unknown object" at the label's (x−8, y). Its label and value text start at x+8.

**Wi-Fi signal strength** (12): a 16×16 glyph at (462,617). That is the value column's rect x with no +2, on the 19-px "Signal strength:" row. It has five bars, each with 4-px outlines shared with the next bar, all ending at y=632. Here 4 of 5 bars are filled. Key:

- `O` = BB814E (outline)
- `f` = DBBAA0 (filled bar)
- `.` = white
- `a` = an anti-aliased edge pixel

```
x: 462 .......... 477
617  ............aaaa
618  ............aaaa
619  .........aaaO..O
620  .........aaaO..O
621  .........aaaO..O
622  ......aaaOffO..O
623  ......aaaOffO..O
624  ...aaaOffOffO..O
625  ...aaaOffOffO..O
626  ...aaaOffOffO..O
627  aaaOffOffOffO..O
628  aaaOffOffOffO..O
629  aaaOffOffOffO..O
630  OffOffOffOffO..O
631  aaaaaaaaaaaaaaaa
632  aOOaOOaOOaOOaOOa
```

- The bars' tops are at about 627, 624, 622, 619 and 617.
- Each bar's top 3 rows are `a` pixels: a grey row (D0C9C2/B0A498, ECECEB/E0DFDE, C8C4C1), a darker outline row (C18C5E, BD8453) and a lighter row (D8B69A, D4AE8F).
- Row 631 is C28F62 / D2AA8A.
- Row 632 alternates D7B496 corner pixels with BB814E.
- So it looks like a scaled image. The empty fifth bar is outlined only.

**GPU engine chevron** ("⌄" before `3D`, `Copy`, `Video Encode`, `Video Decode`): 7×4 px at button + (3,7). For `3D` that is x 258–264, y 143–146. The engine name text starts at button.x+15.

```
92 D6 .. .. .. D6 92      (hex grey levels; .. = white)
.. 85 D6 .. D6 85 ..
.. .. 85 BB 85 .. ..
.. .. .. AD .. .. ..
```

The engine name buttons' dump widths are 15 + text width + 2.

**"Logical processors" caption row** (CPU pane): `% Utilization over 60 seconds` at (255,134) and `100%` right-aligned to 1260. There is no x-axis caption: the row at y=468 is reserved and empty.

## 7. Layout rules (from several captures)

**Vertical stack in the Tables pane** (top 81, bottom 656):

- Title at y=91. Top captions at 134. First graph at 150.
- **Info block** (dynamic values plus static table):
  - It ends at y=636, which is the pane bottom − 20. It starts at `636 − H`.
  - `H = max(dynamic rows·51 − 8, static rows·18)`. Wi-Fi's 19-px signal row adds 1.
  - The values: CPU and Memory start at 491 (3 rows, 145). Disk and Ethernet start at 542 (2 rows, 94). Wi-Fi starts at 527 (6 static rows, 109).
- **Chart area:** y 134 to info top − 10. The graphs flex to fill it:
  - Ethernet: graph 150–516, captions at 519.
  - Wi-Fi: the info block is 15 px taller, so the graph is 352 tall instead of 367.
  - Memory: the composition block (397–480) stays fixed; the usage graph takes the rest (226).
  - Disk: Disk transfer rate is 67 tall; Active time takes the rest (263). The 67 is fixed (section 8).
  - GPU: the content doesn't fit. Its info block is 531–676, and the scrollable content ends at 676 + 20 = 696.
- **Horizontal:** the title is at x=251 (pane x+17); graphs and info at 253 (pane x+19); the right edge is 1262 (pane right − 24), or 1245 with the GPU scrollbar.

**Dynamic values** (label over value):

- **Item width** = max(width of the label at 12 px, width of the value at 20 px). For example, `Utilization` is 53 and `14%` is 38, so the item is 53. `In use (Compressed)` is 140 because of the value `38.0 GB (3.6 GB)`.
- **Row flow** (CPU, Memory, Disk): each row is laid out on its own, from x=253, with 20-px gaps. Columns don't align across rows:

  | Pane | Row 1 | Row 2 | Row 3 |
  |---|---|---|---|
  | CPU | 253 (w53), 326 | 253 (w51), 324 (w55), 399 | 253 |
  | Memory | 253 (w140), 413 | 253 (w93), 366 | 253 (w60), 333 |
  | Disk | 253 (w60), 333 | see the marker item below | none |

  Rows are 51 apart: label at y, value at y+15.
- **Item with a legend marker** (Disk row 2, Network): the marker is at the item x, the text at x+8, and the next item at text x + width + 20. Disk: Read text at 261 (w60), so the Write marker is at 341 and its text at 349.
- **Column layout** (GPU): column 1 at x=253 holds Utilization and GPU Memory. Column 2 at 253 + 101 + 20 = 374 holds Dedicated GPU memory, Shared GPU memory and GPU Temperature. Each column's width is its widest item (101, 127), and every item gets that width.
- **Right edge:** the dynamic block ends at the largest item right edge: CPU 465, Memory 480, Disk 453, GPU 501.
- **Network width only grows:** its Send/Receive column width is the widest value seen so far, it never shrinks, and all adapters share it.

  | Capture | Block right | Width | Source |
  |---|---|---|---|
  | 08–09 dumps | 336 | 8+75 | `8.0 Kbps` width |
  | 10 image | 343 | 8+82 | an earlier value 82 px wide, such as `100 Kbps` |
  | 10 dump onward | 347 | 8+86 | `40.0 Kbps` |

  It stays at 347 even where every value reads `0 Kbps` (60). So the static table's keys sit at 348 in 08–09, 355 in image 10, and 359 from 11 on: the x=346 versus 357 seen in the dumps.

**Static table:**

- The block starts at the dynamic block's right edge.
- Key text x = block x + 12. Value text x = key text x + widest key text + 14.

  | Pane | Key x | Value x |
  |---|---|---|
  | CPU | 477 | 591 |
  | Memory | 492 | 607 |
  | Disk | 465 | 544 |
  | Ethernet (08) | 348 | 453 |
  | Wi-Fi | 359 | 464 |
  | GPU | 513 | 676 |

- Rows are 18 apart from the info-block top. The text origin y is the row y.
- The dump rects are 2 px left of the text and 4 px wider than it: block + 10 for keys, key rect + widest key rect + 10 for values.

## 8. Short window: minimum heights and scrolling

A second capture (`artifacts/reference/taskmgr-short/`, 2026-10-25) was taken with the window shrunk to 1294×394 at 96 DPI, so the Tables pane is 234,81,1052,257. The sidebar scrolls as before (scrollbar 209,88,17,250).

- **Rule:** the flexible element stops shrinking at **80 px**. Once the content no longer fits with it at 80, the pane scrolls: every pane shows the vertical scrollbar (1269,81,17,257), which narrows the content and moves the right edge to 1245.
- **Content height** (pane-relative, so the scroll range) = the stack with the flexible element at 80, then the info block, then 20 px below it.

| Pane | Flexible element at 80 | Fixed parts below it | Info block | Content height |
|---|---|---|---|---|
| CPU | "Logical CPU chart pane" 253,134,992,80: the top captions at 134, the grid 150–199, the empty 15-px band to 214 | none | 224–369 (145) | 308 |
| Memory | `Memory usage` graph 150–230, bottom captions at 232 | composition block 251–335 | 345–490 (145) | 429 |
| Disk | `Active time` graph 150–230 | `Disk transfer rate` caption at 251, graph 267–334 (67) | 359–453 (94) | 392 |
| Ethernet | `Throughput` graph 150–230, bottom captions at 232 | none | 255–349 (94) | 288 |
| Wi-Fi | as Ethernet | none | 255–364 (109) | 303 |
| GPU | none: the same layout as the full-size capture | engine cells, memory graphs | 531–676 (145) | 615 |

- **CPU grid at the minimum:** the grid is 49 tall (150–199). Rows start at 150, 163, 176 and 189 and are 9, 9, 9 and 10 tall, so the last row takes the remainder. The columns are 120 wide at 255 + 124k, and the last one ends at 1243.
- **Fixed heights:** the Disk transfer-rate graph (67), the GPU memory graphs (67), the GPU engine cells and the memory composition block are the same here as in the full-size captures, so they are fixed. Only the first graph flexes.

## Not determinable from these captures

- Sidebar hover and pressed looks.
- The Wi-Fi and GPU 0 sidebar entries' pixels, including the GPU mini-graph colors and the temperature text color. They were always offscreen.
- Whether a visible graph's grid scrolls with the data. The data scrolls at 1 sample/s, but the grid phase doesn't track the sample count across panes. Also how samples-per-grid-column is chosen at other graph widths.
- What value the inner scale marker (`7 MB/s`, `450 Kbps`) represents.
- The GPU Temperature value's pixels. It is clipped below the pane; the dump puts `39 °C` at 374,648,127,28, the 20-px value style.
- Whether the sidebar's network mini-graph send line is dashed.
- The exact rounding rule for the memory composition segments.
