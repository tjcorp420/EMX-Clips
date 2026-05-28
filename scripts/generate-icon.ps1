$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Assets = Join-Path $Root "src\EmxClips\Assets"
$IconPath = Join-Path $Assets "emx-clips.ico"
$LogoPath = Join-Path $Assets "emx-logo.png"

New-Item -ItemType Directory -Force -Path $Assets | Out-Null
Add-Type -AssemblyName System.Drawing

$size = 256
$bitmap = [System.Drawing.Bitmap]::new($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

$bg = $null
$greenBrush = $null
$whiteBrush = $null
$outline = $null
$accent = $null
$rounded = $null
$play = $null
$font = $null
$format = $null
$logo = $null

if (Test-Path $LogoPath) {
    $graphics.Clear([System.Drawing.Color]::Black)
    $logo = [System.Drawing.Image]::FromFile($LogoPath)
    $scale = [Math]::Min($size / $logo.Width, $size / $logo.Height)
    $drawWidth = [int]($logo.Width * $scale)
    $drawHeight = [int]($logo.Height * $scale)
    $x = [int](($size - $drawWidth) / 2)
    $y = [int](($size - $drawHeight) / 2)
    $graphics.DrawImage($logo, $x, $y, $drawWidth, $drawHeight)
} else {
    $bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.Rectangle]::new(0, 0, $size, $size),
        [System.Drawing.Color]::FromArgb(8, 11, 16),
        [System.Drawing.Color]::FromArgb(23, 45, 35),
        45
    )
    $graphics.FillRectangle($bg, 0, 0, $size, $size)

    $green = [System.Drawing.Color]::FromArgb(35, 235, 125)
    $greenBrush = [System.Drawing.SolidBrush]::new($green)
    $whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $outline = [System.Drawing.Pen]::new($green, 13)
    $accent = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(116, 255, 178), 7)

    $rounded = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $radius = 42
    $rounded.AddArc(15, 15, $radius, $radius, 180, 90)
    $rounded.AddArc($size - 15 - $radius, 15, $radius, $radius, 270, 90)
    $rounded.AddArc($size - 15 - $radius, $size - 15 - $radius, $radius, $radius, 0, 90)
    $rounded.AddArc(15, $size - 15 - $radius, $radius, $radius, 90, 90)
    $rounded.CloseFigure()
    $graphics.DrawPath($outline, $rounded)

    $play = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $play.AddPolygon(@(
        [System.Drawing.Point]::new(92, 78),
        [System.Drawing.Point]::new(92, 178),
        [System.Drawing.Point]::new(178, 128)
    ))
    $graphics.FillPath($greenBrush, $play)

    $font = [System.Drawing.Font]::new("Segoe UI", 44, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = [System.Drawing.StringFormat]::new()
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $graphics.DrawString("EMX", $font, $whiteBrush, [System.Drawing.RectangleF]::new(0, 176, $size, 64), $format)

    $graphics.DrawLine($accent, 42, 50, 92, 50)
    $graphics.DrawLine($accent, 164, 206, 214, 206)
}

$pngStream = [System.IO.MemoryStream]::new()
$bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $pngStream.ToArray()

$file = [System.IO.File]::Create($IconPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]1)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$pngBytes.Length)
    $writer.Write([UInt32]22)
    $writer.Write($pngBytes)
} finally {
    $writer.Dispose()
    $file.Dispose()
    $pngStream.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
    if ($bg) { $bg.Dispose() }
    if ($greenBrush) { $greenBrush.Dispose() }
    if ($whiteBrush) { $whiteBrush.Dispose() }
    if ($outline) { $outline.Dispose() }
    if ($accent) { $accent.Dispose() }
    if ($rounded) { $rounded.Dispose() }
    if ($play) { $play.Dispose() }
    if ($font) { $font.Dispose() }
    if ($format) { $format.Dispose() }
    if ($logo) { $logo.Dispose() }
}

Write-Host "Generated $IconPath"
