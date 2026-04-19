param(
    [string] $SourcePath = (Join-Path (Split-Path -Parent $PSScriptRoot) "src\FogSwitcher\Assets\app_icon.png"),
    [string] $DestinationPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "src\FogSwitcher\obj\app.ico")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

function ClampByte {
    param(
        [Parameter(Mandatory = $true)]
        [int] $Value
    )

    if ($Value -lt 0) { return [byte]0 }
    if ($Value -gt 255) { return [byte]255 }
    return [byte]$Value
}

function RemoveWhiteMatte {
    param(
        [Parameter(Mandatory = $true)]
        [System.Drawing.Bitmap] $Bitmap
    )

    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            $pixel = $Bitmap.GetPixel($x, $y)
            if ($pixel.A -eq 0) {
                continue
            }

            $max = [Math]::Max($pixel.R, [Math]::Max($pixel.G, $pixel.B))
            $min = [Math]::Min($pixel.R, [Math]::Min($pixel.G, $pixel.B))
            $average = [int](($pixel.R + $pixel.G + $pixel.B) / 3)
            $isNearNeutral = ($max - $min) -le 12

            if ((-not $isNearNeutral) -or $average -lt 240) {
                continue
            }

            if ($average -ge 249) {
                $Bitmap.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
                continue
            }

            $alpha = [int][Math]::Round(255.0 * (249 - $average) / 9.0)
            if ($alpha -le 0) {
                $Bitmap.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
                continue
            }

            $opacity = $alpha / 255.0
            $r = ClampByte ([int][Math]::Round(($pixel.R - (255.0 * (1.0 - $opacity))) / $opacity))
            $g = ClampByte ([int][Math]::Round(($pixel.G - (255.0 * (1.0 - $opacity))) / $opacity))
            $b = ClampByte ([int][Math]::Round(($pixel.B - (255.0 * (1.0 - $opacity))) / $opacity))

            $Bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, $r, $g, $b))
        }
    }
}

function GetVisibleBounds {
    param(
        [Parameter(Mandatory = $true)]
        [System.Drawing.Bitmap] $Bitmap
    )

    $minX = $Bitmap.Width
    $minY = $Bitmap.Height
    $maxX = -1
    $maxY = -1

    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -le 0) {
                continue
            }

            if ($x -lt $minX) { $minX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }

    if ($maxX -lt 0 -or $maxY -lt 0) {
        return [System.Drawing.Rectangle]::FromLTRB(0, 0, $Bitmap.Width, $Bitmap.Height)
    }

    return [System.Drawing.Rectangle]::FromLTRB($minX, $minY, $maxX + 1, $maxY + 1)
}

$frameSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$contentScale = 0.98

$frames = New-Object System.Collections.Generic.List[object]

if (-not (Test-Path $SourcePath)) {
    throw "Missing icon source: $SourcePath"
}

$source = [System.Drawing.Bitmap]::FromFile($SourcePath)
try {
    $visibleBounds = GetVisibleBounds -Bitmap $source

    foreach ($size in $frameSizes) {
        $target = New-Object System.Drawing.Bitmap(
            $size,
            $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

        try {
            $graphics = [System.Drawing.Graphics]::FromImage($target)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

                $contentSize = [Math]::Max(1, [int][Math]::Round($size * $contentScale))
                $scale = [Math]::Min($contentSize / [double]$visibleBounds.Width, $contentSize / [double]$visibleBounds.Height)
                $drawWidth = [int][Math]::Round($visibleBounds.Width * $scale)
                $drawHeight = [int][Math]::Round($visibleBounds.Height * $scale)
                $offsetX = [int][Math]::Floor(($size - $drawWidth) / 2)
                $offsetY = [int][Math]::Floor(($size - $drawHeight) / 2)

                $destinationRect = New-Object System.Drawing.Rectangle($offsetX, $offsetY, $drawWidth, $drawHeight)
                $graphics.DrawImage($source, $destinationRect, $visibleBounds, [System.Drawing.GraphicsUnit]::Pixel)
            }
            finally {
                $graphics.Dispose()
            }

            RemoveWhiteMatte -Bitmap $target

            $memory = New-Object System.IO.MemoryStream
            try {
                $target.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
                [byte[]] $pngBytes = $memory.ToArray()
                $frames.Add([pscustomobject]@{
                    Width = $target.Width
                    Height = $target.Height
                    PngBytes = $pngBytes
                })
            }
            finally {
                $memory.Dispose()
            }
        }
        finally {
            $target.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

$destinationDirectory = Split-Path -Parent $DestinationPath
if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
}

$stream = [System.IO.File]::Create($DestinationPath)
$writer = New-Object System.IO.BinaryWriter($stream)

try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$frames.Count)

    $offset = 6 + ($frames.Count * 16)
    foreach ($frame in $frames) {
        if ($frame.Width -ge 256) { $widthByte = [byte]0 } else { $widthByte = [byte]$frame.Width }
        if ($frame.Height -ge 256) { $heightByte = [byte]0 } else { $heightByte = [byte]$frame.Height }

        $writer.Write($widthByte)
        $writer.Write($heightByte)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frame.PngBytes.Length)
        $writer.Write([UInt32]$offset)

        $offset += $frame.PngBytes.Length
    }

    foreach ($frame in $frames) {
        $writer.Write($frame.PngBytes)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Output "Generated $DestinationPath from $SourcePath"
