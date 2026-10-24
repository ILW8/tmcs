<#
.SYNOPSIS
  Generates src/Tmcs/tmcs.ico: a rounded dark-slate square with a rising green line graph.
.DESCRIPTION
  Original artwork drawn with System.Drawing. Each size is rendered natively (not scaled) and stored
  PNG-encoded in a multi-image ICO. Run: pwsh -File tools/make-icon.ps1
#>
param(
    [string]$OutPath = (Join-Path $PSScriptRoot '..\src\Tmcs\tmcs.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 256

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        # Background: rounded dark-slate square.
        $margin = [Math]::Max(0.5, $size * 0.03)
        $bg = New-RoundedRect $margin $margin ($size - 2 * $margin) ($size - 2 * $margin) ($size * 0.2)
        $bgBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 30, 41, 59))
        $g.FillPath($bgBrush, $bg)

        # Faint horizontal grid lines, only where they stay legible.
        if ($size -ge 32) {
            $gridPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(70, 148, 163, 184), [Math]::Max(1.0, $size / 128.0))
            foreach ($fy in 0.3, 0.5, 0.7) {
                $g.DrawLine($gridPen, [single]($size * 0.14), [single]($size * $fy), [single]($size * 0.86), [single]($size * $fy))
            }
            $gridPen.Dispose()
        }

        # Rising line graph: area fill under a polyline.
        $fx = 0.16, 0.34, 0.48, 0.66, 0.84
        $fy = 0.74, 0.54, 0.62, 0.38, 0.24
        $pts = for ($i = 0; $i -lt $fx.Count; $i++) {
            [System.Drawing.PointF]::new([single]($size * $fx[$i]), [single]($size * $fy[$i]))
        }
        $area = [System.Drawing.PointF[]]($pts + [System.Drawing.PointF]::new([single]($size * 0.84), [single]($size * 0.8)) + [System.Drawing.PointF]::new([single]($size * 0.16), [single]($size * 0.8)))
        $areaBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(70, 74, 222, 128))
        $g.FillPolygon($areaBrush, $area)

        $lineWidth = [Math]::Max(1.6, $size * 0.075)
        $linePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 74, 222, 128), $lineWidth)
        $linePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLines($linePen, [System.Drawing.PointF[]]$pts)

        # Marker on the latest sample.
        $dot = $lineWidth * 1.35
        $last = $pts[-1]
        $g.FillEllipse([System.Drawing.Brushes]::White, [single]($last.X - $dot / 2), [single]($last.Y - $dot / 2), [single]$dot, [single]$dot)

        $linePen.Dispose(); $areaBrush.Dispose(); $bgBrush.Dispose(); $bg.Dispose()
    }
    finally {
        $g.Dispose()
    }
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$images = foreach ($s in $sizes) { , (New-IconPng $s) }

$fullPath = [System.IO.Path]::GetFullPath($OutPath)
$fs = [System.IO.File]::Create($fullPath)
$bw = [System.IO.BinaryWriter]::new($fs)
try {
    # ICONDIR
    $bw.Write([uint16]0)               # reserved
    $bw.Write([uint16]1)               # type: icon
    $bw.Write([uint16]$sizes.Count)    # image count

    # ICONDIRENTRY x N
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256
        $bw.Write([byte]$dim)          # width
        $bw.Write([byte]$dim)          # height
        $bw.Write([byte]0)             # palette colors
        $bw.Write([byte]0)             # reserved
        $bw.Write([uint16]1)           # color planes
        $bw.Write([uint16]32)          # bits per pixel
        $bw.Write([uint32]$images[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $images[$i].Length
    }

    # Image data (PNG)
    foreach ($img in $images) { $bw.Write([byte[]]$img) }
}
finally {
    $bw.Dispose()
    $fs.Dispose()
}

Write-Host "Wrote $fullPath ($((Get-Item $fullPath).Length) bytes)"
