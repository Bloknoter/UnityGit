<#
.SYNOPSIS
Draws UnityGit's action icons into Editor/UI/Icons.

.DESCRIPTION
Each icon is a 32x32 PNG of anti-aliased white 2px lines with round caps and joins, on a transparent background.
The window shows them at 16x16 and tints them in GitWindow.uss, so only the alpha channel carries the shape.

Coordinates work like SVG: pixel (i, j) covers [i, i+1] x [j, j+1]. Straight lines sit on odd coordinates, so a
2px line covers two whole pixels (2k and 2k+1) and stays one crisp pixel wide when Unity draws the icon at 16x16.

Unity ignores this Tools~ folder. Run it from anywhere (Windows PowerShell 5.1 or later) and commit the PNGs:
    powershell -ExecutionPolicy Bypass -File Tools~/make-icons.ps1
#>
param([string]$OutDir = (Join-Path $PSScriptRoot '..\Editor\UI\Icons'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Size = 32
$Stroke = 2

function New-Pt([double]$x, [double]$y) { New-Object System.Drawing.PointF ([single]$x), ([single]$y) }

function Draw-Line($g, $pen, [double]$x1, [double]$y1, [double]$x2, [double]$y2) {
    $g.DrawLine($pen, [single]$x1, [single]$y1, [single]$x2, [single]$y2)
}

# A polyline through x1, y1, x2, y2, ...
function Draw-Polyline($g, $pen, [double[]]$xy) {
    $points = for ($i = 0; $i -lt $xy.Length; $i += 2) { New-Pt $xy[$i] $xy[$i + 1] }
    $g.DrawLines($pen, [System.Drawing.PointF[]]$points)
}

function Draw-Circle($g, $pen, [double]$cx, [double]$cy, [double]$r) {
    $g.DrawEllipse($pen, [single]($cx - $r), [single]($cy - $r), [single](2 * $r), [single](2 * $r))
}

# Angles in degrees, clockwise from +x (y points down), as System.Drawing measures them.
function Add-Arc($path, [double]$cx, [double]$cy, [double]$r, [double]$start, [double]$sweep) {
    $path.AddArc([single]($cx - $r), [single]($cy - $r), [single](2 * $r), [single](2 * $r), [single]$start, [single]$sweep)
}

function Add-Line($path, [double]$x1, [double]$y1, [double]$x2, [double]$y2) {
    $path.AddLine([single]$x1, [single]$y1, [single]$x2, [single]$y2)
}

function Draw-Path($g, $pen, [scriptblock]$build) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    try {
        & $build $path
        $g.DrawPath($pen, $path)
    } finally {
        $path.Dispose()
    }
}

# Two lines meeting at the tip (x, y): an arrowhead pointing along the direction angle (degrees, as above).
function Draw-Arrowhead($g, $pen, [double]$x, [double]$y, [double]$angle, [double]$length) {
    foreach ($side in 1, -1) {
        $a = ($angle + 180 + 45 * $side) * [Math]::PI / 180
        Draw-Line $g $pen $x $y ($x + $length * [Math]::Cos($a)) ($y + $length * [Math]::Sin($a))
    }
}

$icons = [ordered]@{
    # Git branch: a trunk between two nodes, and a third node whose line curves into the trunk.
    branch = {
        param($g, $pen)
        Draw-Circle $g $pen 9 7 3
        Draw-Circle $g $pen 9 25 3
        Draw-Line $g $pen 9 10 9 22
        Draw-Circle $g $pen 23 9 3
        Draw-Path $g $pen { param($p) $p.AddBezier((New-Pt 23 12), (New-Pt 23 18), (New-Pt 9 15), (New-Pt 9 21)) }
    }
    # Commit: a node on a line, git's commit mark.
    commit = {
        param($g, $pen)
        Draw-Circle $g $pen 16 15 5
        Draw-Line $g $pen 4 15 11 15
        Draw-Line $g $pen 21 15 28 15
    }
    # Fetch: a cloud, open at the bottom, with an arrow coming down out of it.
    fetch = {
        param($g, $pen)
        Draw-Path $g $pen {
            param($p)
            Add-Line $p 11 19 8 19
            Add-Arc $p 8 14 5 90 180
            Add-Arc $p 15 11 6.5 200 140
            Add-Arc $p 22 14 5 270 180
            Add-Line $p 22 19 19 19
        }
        Draw-Line $g $pen 15 13 15 28
        Draw-Arrowhead $g $pen 15 28 90 5
    }
    # Pull: an arrow down into a tray.
    pull = {
        param($g, $pen)
        Draw-Polyline $g $pen @(5, 19, 5, 27, 25, 27, 25, 19)
        Draw-Line $g $pen 15 5 15 21
        Draw-Arrowhead $g $pen 15 21 90 6
    }
    # Push: an arrow up out of a tray.
    push = {
        param($g, $pen)
        Draw-Polyline $g $pen @(5, 19, 5, 27, 25, 27, 25, 19)
        Draw-Line $g $pen 15 21 15 5
        Draw-Arrowhead $g $pen 15 5 270 6
    }
    # Refresh: a circular arrow running clockwise, its head at the top right.
    refresh = {
        param($g, $pen)
        # The arc ends at 315 degrees, (23.07, 8.93). The head's tip is that point snapped to (23, 9), so its two arms
        # (one up, one left) land on whole pixels. It points along the arc's direction there, 315 + 90 degrees.
        Draw-Path $g $pen { param($p) Add-Arc $p 16 16 10 30 285 }
        Draw-Arrowhead $g $pen 23 9 45 6
    }
    # Cancel: a cross.
    cancel = {
        param($g, $pen)
        Draw-Line $g $pen 9 9 23 23
        Draw-Line $g $pen 23 9 9 23
    }
    # Init: a plus.
    init = {
        param($g, $pen)
        Draw-Line $g $pen 15 6 15 24
        Draw-Line $g $pen 6 15 24 15
    }
    # LFS: stacked files, a page with a folded corner in front of another.
    lfs = {
        param($g, $pen)
        Draw-Polyline $g $pen @(11, 11, 11, 5, 25, 5, 25, 21, 19, 21)
        Draw-Path $g $pen {
            param($p)
            Add-Line $p 5 11 15 11
            Add-Line $p 15 11 19 15
            Add-Line $p 19 15 19 27
            Add-Line $p 19 27 5 27
            $p.CloseFigure()
        }
        Draw-Polyline $g $pen @(15, 11, 15, 15, 19, 15)
    }
    # Lock: a padlock with a keyhole.
    lock = {
        param($g, $pen)
        Draw-Path $g $pen {
            param($p)
            Add-Line $p 11 13 11 9
            Add-Arc $p 15 9 4 180 180
            Add-Line $p 19 9 19 13
        }
        Draw-Path $g $pen {
            param($p)
            $l = 7; $t = 13; $rt = 23; $b = 27; $c = 2
            Add-Arc $p ($l + $c) ($t + $c) $c 180 90
            Add-Arc $p ($rt - $c) ($t + $c) $c 270 90
            Add-Arc $p ($rt - $c) ($b - $c) $c 0 90
            Add-Arc $p ($l + $c) ($b - $c) $c 90 90
            $p.CloseFigure()
        }
        Draw-Line $g $pen 15 18 15 21
    }
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

foreach ($name in $icons.Keys) {
    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single]$Stroke)
    try {
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        & $icons[$name] $g $pen
    } finally {
        $pen.Dispose()
        $g.Dispose()
    }

    # The shape lives in alpha alone: every pixel is made white so that tinted edges carry no dark fringe.
    for ($y = 0; $y -lt $Size; $y++) {
        for ($x = 0; $x -lt $Size; $x++) {
            $alpha = $bitmap.GetPixel($x, $y).A
            $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, 255, 255, 255))
        }
    }

    $file = Join-Path $OutDir "$name.png"
    $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Host "Wrote $file"
}
