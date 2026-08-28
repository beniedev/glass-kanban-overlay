$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outDir = Join-Path $root "docs\assets"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outFile = Join-Path $outDir "glass-kanban-overlay-preview.png"

function Get-ColumnTasks([string]$path, [string]$column, [int]$max = 4) {
    $tasks = New-Object System.Collections.Generic.List[string]
    $inColumn = $false
    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -match '^\s*##\s+(.+?)\s*$') {
            $inColumn = $matches[1] -eq $column
            continue
        }

        if ($inColumn -and $line -match '^\s*-\s+\[[ xX]\]\s+(.+?)\s*$') {
            $tasks.Add($matches[1])
            if ($tasks.Count -ge $max) {
                break
            }
        }
    }

    return $tasks.ToArray()
}

$simplePath = Join-Path $root "examples\simple-board.md"
$multiPath = Join-Path $root "examples\multi-vault-style-board.md"
$datedPath = Join-Path $root "examples\dated-completed-section.md"

$simpleTasks = Get-ColumnTasks $simplePath "TODO" 3
$focusTasks = Get-ColumnTasks $multiPath "Focus" 2
$datedTasks = Get-ColumnTasks $datedPath "TODO" 2

$width = 1280
$height = 720
$bitmap = [System.Drawing.Bitmap]::new($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Fill-RoundRect($brush, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-RoundedRect $x $y $w $h $r
    $graphics.FillPath($brush, $path)
    $path.Dispose()
}

function Stroke-RoundRect($pen, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-RoundedRect $x $y $w $h $r
    $graphics.DrawPath($pen, $path)
    $path.Dispose()
}

function Draw-Text([string]$text, [float]$x, [float]$y, [float]$size, $color, [string]$style = "Regular") {
    $font = [System.Drawing.Font]::new("Segoe UI", [float]$size, [System.Drawing.FontStyle]::$style, [System.Drawing.GraphicsUnit]::Point)
    $brush = [System.Drawing.SolidBrush]::new($color)
    $graphics.DrawString($text, $font, $brush, $x, $y)
    $brush.Dispose()
    $font.Dispose()
}

function Draw-TextBox([string]$text, [float]$x, [float]$y, [float]$w, [float]$h, [float]$size, $color, [string]$style = "Regular") {
    $font = [System.Drawing.Font]::new("Segoe UI", [float]$size, [System.Drawing.FontStyle]::$style, [System.Drawing.GraphicsUnit]::Point)
    $brush = [System.Drawing.SolidBrush]::new($color)
    $format = [System.Drawing.StringFormat]::new()
    $format.Trimming = [System.Drawing.StringTrimming]::EllipsisWord
    $graphics.DrawString($text, $font, $brush, [System.Drawing.RectangleF]::new($x, $y, $w, $h), $format)
    $format.Dispose()
    $brush.Dispose()
    $font.Dispose()
}

function Draw-Button([string]$text, [float]$x, [float]$y, [float]$w, [float]$h = 32) {
    $buttonBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(176, 237, 239, 242))
    $buttonPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(210, 255, 255, 255), 1)
    Fill-RoundRect $buttonBrush $x $y $w $h 9
    Stroke-RoundRect $buttonPen $x $y $w $h 9
    Draw-TextBox $text ($x + 10) ($y + 7) ($w - 20) ($h - 8) 9 ([System.Drawing.Color]::FromArgb(30, 39, 52))
    $buttonPen.Dispose()
    $buttonBrush.Dispose()
}

function Draw-TaskCard([string]$text, [float]$x, [float]$y, [float]$w, [float]$h) {
    $cardBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(42, 255, 255, 255))
    $cardPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(34, 255, 255, 255), 1)
    Fill-RoundRect $cardBrush $x $y $w $h 8
    Stroke-RoundRect $cardPen $x $y $w $h 8

    $checkBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(242, 248, 250, 252))
    Fill-RoundRect $checkBrush ($x + 12) ($y + 14) 14 14 4
    Draw-TextBox $text ($x + 38) ($y + 10) ($w - 78) ($h - 12) 9.5 ([System.Drawing.Color]::FromArgb(246, 248, 252))
    Draw-Button "..." ($x + $w - 36) ($y + 10) 28 28

    $checkBrush.Dispose()
    $cardPen.Dispose()
    $cardBrush.Dispose()
}

function Draw-Column([string]$title, [string[]]$tasks, [float]$x, [float]$y, [float]$w, [float]$h) {
    $columnBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(22, 255, 255, 255))
    $columnPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(48, 255, 255, 255), 1)
    Fill-RoundRect $columnBrush $x $y $w $h 4
    Stroke-RoundRect $columnPen $x $y $w $h 4

    Draw-TextBox $title ($x + 12) ($y + 12) ($w - 52) 24 10 ([System.Drawing.Color]::FromArgb(250, 252, 255)) "Bold"
    Draw-Button "..." ($x + $w - 42) ($y + 10) 28 28

    $cardY = $y + 56
    foreach ($task in $tasks) {
        Draw-TaskCard $task ($x + 12) $cardY ($w - 24) 54
        $cardY += 68
    }

    Draw-Button "+ Add card" ($x + 12) ($y + $h - 36) ($w - 24) 26

    $columnPen.Dispose()
    $columnBrush.Dispose()
}

function Draw-WindowFrame([float]$x, [float]$y, [float]$w, [float]$h, [int]$r, [int]$g, [int]$b, [float]$opacity = 0.82) {
    $shadow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(34, 0, 0, 0))
    Fill-RoundRect $shadow ($x + 8) ($y + 8) $w $h 4
    $shadow.Dispose()

    $alpha = [Math]::Round(255 * $opacity)
    $panelBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($alpha, $r, $g, $b))
    $panelPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(54, 255, 255, 255), 1)
    Fill-RoundRect $panelBrush $x $y $w $h 4
    Stroke-RoundRect $panelPen $x $y $w $h 4
    $panelPen.Dispose()
    $panelBrush.Dispose()
}

function Draw-Slider([float]$x, [float]$y, [float]$w, [float]$knob) {
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(210, 231, 235, 242), 3)
    $graphics.DrawLine($pen, $x, $y, ($x + $w), $y)
    $knobBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(250, 255, 255, 255))
    $graphics.FillEllipse($knobBrush, ($x + $w * $knob - 6), ($y - 6), 12, 12)
    $knobBrush.Dispose()
    $pen.Dispose()
}

function Draw-SummaryWindow() {
    Draw-WindowFrame 34 36 760 500 18 24 36 0.84
    Draw-Text "Board" 58 62 24 ([System.Drawing.Color]::FromArgb(250, 252, 255)) "Bold"
    Draw-Text "Kanban" 152 72 12 ([System.Drawing.Color]::FromArgb(184, 193, 207)) "Bold"
    Draw-Button "Split all" 520 60 62
    Draw-Button "Refresh" 590 60 68
    Draw-Button "Settings" 666 60 72
    Draw-Button "x" 746 60 28
    Draw-Text "Opacity" 58 111 9.5 ([System.Drawing.Color]::FromArgb(215, 224, 236))
    Draw-Slider 114 118 645 0.76

    Draw-Column "Simple / TODO" $simpleTasks 58 136 310 328
    Draw-Column "Multi Vault / Focus" $focusTasks 380 136 310 328
    Draw-Column "Completed / TODO" $datedTasks 702 136 310 328
    Draw-Text "Refreshed 20:00:00" 58 486 8.5 ([System.Drawing.Color]::FromArgb(215, 224, 236))
}

function Draw-SplitWindow([string]$title, [string]$note, [string[]]$tasks, [float]$x, [float]$y, [float]$w, [float]$h, [int]$r, [int]$g, [int]$b) {
    Draw-WindowFrame $x $y $w $h $r $g $b 0.82
    Draw-TextBox $title ($x + 24) ($y + 24) ($w - 145) 42 23 ([System.Drawing.Color]::FromArgb(250, 252, 255)) "Bold"
    Draw-TextBox $note ($x + 24) ($y + 66) ($w - 60) 24 13 ([System.Drawing.Color]::FromArgb(184, 193, 207)) "Bold"
    Draw-Button "Lock" ($x + $w - 156) ($y + 24) 92 30
    Draw-Button "..." ($x + $w - 56) ($y + 24) 32 30
    Draw-Text "Opacity" ($x + 24) ($y + 108) 9 ([System.Drawing.Color]::FromArgb(215, 224, 236))
    Draw-Slider ($x + 82) ($y + 116) ($w - 110) 0.72

    $cardY = $y + 142
    foreach ($task in $tasks) {
        Draw-TaskCard $task ($x + 24) $cardY ($w - 48) 48
        $cardY += 60
    }

    Draw-Button "+ Add card" ($x + 24) ($y + $h - 70) ($w - 48) 28
    Draw-Text "Refreshed 20:00:00" ($x + 24) ($y + $h - 32) 8.5 ([System.Drawing.Color]::FromArgb(215, 224, 236))
}

$bgRect = [System.Drawing.Rectangle]::new(0, 0, $width, $height)
$bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new($bgRect, [System.Drawing.Color]::FromArgb(246, 248, 252), [System.Drawing.Color]::FromArgb(226, 233, 242), 35)
$graphics.FillRectangle($bg, $bgRect)
$bg.Dispose()

$accent1 = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(28, 86, 142, 186))
$accent2 = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(24, 236, 190, 90))
$graphics.FillEllipse($accent1, 42, 80, 480, 360)
$graphics.FillEllipse($accent2, 902, 190, 300, 260)
$accent1.Dispose()
$accent2.Dispose()

Draw-SummaryWindow
Draw-SplitWindow "Simple" "TODO" $simpleTasks 830 56 360 558 18 24 36
Draw-SplitWindow "Multi Vault" "Focus" $focusTasks 458 396 360 300 18 41 34

$bitmap.Save($outFile, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

Write-Output $outFile
