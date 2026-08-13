<#
.SYNOPSIS
    Generates src/GroundControl/Resources/app.ico (the exe + tray icon).

.DESCRIPTION
    The icon is drawn in code rather than checked in as an opaque binary, so it can be
    tweaked and regenerated. Sizes 16-48 are written as 32-bit BMP entries (the format
    every Windows shell surface reads); 256 is written as a PNG entry, as is conventional.

    Run with Windows PowerShell (System.Drawing):
        powershell -ExecutionPolicy Bypass -File build\make-icon.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $OutputPath) {
    $root = Split-Path -Parent $MyInvocation.MyCommand.Path
    $OutputPath = Join-Path $root '..\src\GroundControl\Resources\app.ico'
}
Add-Type -AssemblyName System.Drawing

# ---------------------------------------------------------------- drawing
function New-RoundedPath {
    param([double]$X, [double]$Y, [double]$W, [double]$H, [double]$R)

    $R = [Math]::Min($R, [Math]::Min($W, $H) / 2)
    $d = $R * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($R -le 0.01) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF $X, $Y, $W, $H))
    } else {
        $path.AddArc([float]$X,             [float]$Y,             [float]$d, [float]$d, 180, 90)
        $path.AddArc([float]($X + $W - $d), [float]$Y,             [float]$d, [float]$d, 270, 90)
        $path.AddArc([float]($X + $W - $d), [float]($Y + $H - $d), [float]$d, [float]$d,   0, 90)
        $path.AddArc([float]$X,             [float]($Y + $H - $d), [float]$d, [float]$d,  90, 90)
        $path.CloseFigure()
    }
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [double]$Size

    # Dark rounded backdrop, matching the overlay's #15151B / #1F2430 palette.
    $bg = New-RoundedPath -X ($s * 0.02) -Y ($s * 0.02) -W ($s * 0.96) -H ($s * 0.96) -R ($s * 0.22)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 0, 0),
        (New-Object System.Drawing.PointF ([float]$s), ([float]$s)),
        [System.Drawing.Color]::FromArgb(255, 0x28, 0x2E, 0x3C),
        [System.Drawing.Color]::FromArgb(255, 0x15, 0x18, 0x20))
    $g.FillPath($bgBrush, $bg)

    # Three "windows", echoing the exposé layout: one large, one tall, one wide.
    $r = $s * 0.055
    $tiles = @(
        @{ X = 0.14; Y = 0.16; W = 0.46; H = 0.36; C = @(0x3B, 0x9E, 0xFF); A = 255 },
        @{ X = 0.64; Y = 0.16; W = 0.22; H = 0.36; C = @(0xEC, 0xED, 0xF2); A = 235 },
        @{ X = 0.14; Y = 0.58; W = 0.72; H = 0.26; C = @(0x9A, 0xA3, 0xB5); A = 235 }
    )
    foreach ($t in $tiles) {
        $path = New-RoundedPath -X ($s * $t.X) -Y ($s * $t.Y) -W ($s * $t.W) -H ($s * $t.H) -R $r
        $brush = New-Object System.Drawing.SolidBrush (
            [System.Drawing.Color]::FromArgb($t.A, $t.C[0], $t.C[1], $t.C[2]))
        $g.FillPath($brush, $path)
        $brush.Dispose()
        $path.Dispose()
    }

    $bgBrush.Dispose()
    $bg.Dispose()
    $g.Dispose()
    return $bmp
}

# ---------------------------------------------------------------- ICO encoding
function Get-BmpEntryBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                             [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $pixels = New-Object byte[] ($data.Stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    } finally {
        $Bitmap.UnlockBits($data)
    }

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream

    # BITMAPINFOHEADER — height is doubled to cover the (unused) AND mask.
    $andRowBytes = [Math]::Floor(($w + 31) / 32) * 4
    $xorSize = $w * $h * 4
    $andSize = $andRowBytes * $h

    $writer.Write([int]40)          # biSize
    $writer.Write([int]$w)          # biWidth
    $writer.Write([int]($h * 2))    # biHeight (XOR + AND)
    $writer.Write([int16]1)         # biPlanes
    $writer.Write([int16]32)        # biBitCount
    $writer.Write([int]0)           # biCompression = BI_RGB
    $writer.Write([int]($xorSize + $andSize))
    $writer.Write([int]2835)        # biXPelsPerMeter
    $writer.Write([int]2835)        # biYPelsPerMeter
    $writer.Write([int]0)           # biClrUsed
    $writer.Write([int]0)           # biClrImportant

    # XOR data, bottom-up (LockBits hands back top-down rows).
    for ($y = $h - 1; $y -ge 0; $y--) {
        $writer.Write($pixels, $y * $data.Stride, $w * 4)
    }

    # AND mask: all zeros — the alpha channel already carries transparency.
    $writer.Write((New-Object byte[] $andSize), 0, $andSize)

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return , $bytes
}

function Get-PngEntryBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $stream = New-Object System.IO.MemoryStream
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return , $bytes
}

# ---------------------------------------------------------------- build the file
$sizes = @(16, 20, 24, 32, 40, 48, 64, 256)
$images = @()

foreach ($size in $sizes) {
    $bmp = New-IconBitmap -Size $size
    $bytes = if ($size -ge 256) { Get-PngEntryBytes -Bitmap $bmp } else { Get-BmpEntryBytes -Bitmap $bmp }
    $images += [pscustomobject]@{ Size = $size; Bytes = $bytes }
    $bmp.Dispose()
}

$outDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$OutputPath = Join-Path (Resolve-Path -LiteralPath $outDir).Path (Split-Path -Leaf $OutputPath)

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out

$w.Write([int16]0)                  # reserved
$w.Write([int16]1)                  # type: icon
$w.Write([int16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim)            # width  (0 means 256)
    $w.Write([byte]$dim)            # height
    $w.Write([byte]0)               # palette size
    $w.Write([byte]0)               # reserved
    $w.Write([int16]1)              # colour planes
    $w.Write([int16]32)             # bits per pixel
    $w.Write([int]$img.Bytes.Length)
    $w.Write([int]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write($img.Bytes, 0, $img.Bytes.Length) }

$w.Flush()
[System.IO.File]::WriteAllBytes($OutputPath, $out.ToArray())
$w.Dispose()
$out.Dispose()

Write-Host "Wrote $OutputPath ($($images.Count) sizes: $($sizes -join ', '))"
