# Generates app icon assets at all sizes required by the WinUI 3 packaging
# manifest by resizing the source PNG (App/vault.png) with high-quality
# bicubic interpolation. Writes output to ../src/ToshanVault.App/Assets.
#
# Usage: pwsh -File tools/GenerateVaultIcon.ps1

Add-Type -AssemblyName System.Drawing

$Assets = Join-Path $PSScriptRoot '..\src\ToshanVault.App\Assets'
$Assets = (Resolve-Path $Assets).Path

$SourcePath = Join-Path $PSScriptRoot '..\App\vault.png'
if (-not (Test-Path $SourcePath)) {
    throw "Source image not found: $SourcePath"
}
$SourceImage = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePath).Path)

function New-VaultBitmap {
    param([int]$Size, [bool]$Transparent = $false)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $bmp.SetResolution(96, 96)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $g.DrawImage($SourceImage, 0, 0, $Size, $Size)
    $g.Dispose()
    return $bmp
}

function Save-Png {
    param([int]$Size, [string]$Name, [bool]$Transparent = $false)
    $b = New-VaultBitmap -Size $Size -Transparent:$Transparent
    $out = Join-Path $Assets $Name
    $b.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    "  $Name ($Size x $Size)"
}

function Save-Wide {
    param([int]$W, [int]$H, [string]$Name)
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::White)
    $iconSize = [int]($H * 0.85)
    $icon = New-VaultBitmap -Size $iconSize
    $g.DrawImage($icon, [int](($W - $iconSize) / 2), [int](($H - $iconSize) / 2), $iconSize, $iconSize)
    $icon.Dispose()
    if ($W -gt $H * 1.5) {
        $textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 60, 60, 60))
        $fontSize = [int]($H * 0.22)
        $font = New-Object System.Drawing.Font 'Segoe UI Semibold', $fontSize, ([System.Drawing.GraphicsUnit]::Pixel)
        $sf = New-Object System.Drawing.StringFormat
        $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
        $sf.Alignment = [System.Drawing.StringAlignment]::Near
        $textRect = New-Object System.Drawing.RectangleF -ArgumentList ([float](($W + $iconSize) / 2 + $H * 0.05)), ([float]0), ([float]($W / 2)), ([float]$H)
        $g.DrawString('Toshan Vault', $font, $textBrush, $textRect, $sf)
        $font.Dispose()
        $textBrush.Dispose()
        $sf.Dispose()
    }
    $g.Dispose()
    $out = Join-Path $Assets $Name
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "  $Name ($W x $H)"
}

function Save-Ico {
    param([int[]]$Sizes, [string]$Name)
    $bitmaps = @()
    foreach ($s in $Sizes) { $bitmaps += ,(New-VaultBitmap -Size $s -Transparent $true) }

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]$bitmaps.Count)

    $entryStart = $ms.Position
    foreach ($b in $bitmaps) { $bw.Write((New-Object byte[] 16)) }

    $imageBlobs = @()
    foreach ($b in $bitmaps) {
        $tmp = New-Object System.IO.MemoryStream
        $b.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
        $imageBlobs += ,$tmp.ToArray()
        $tmp.Dispose()
    }

    $offsets = @()
    foreach ($blob in $imageBlobs) {
        $offsets += $ms.Position
        $bw.Write($blob)
    }

    for ($i = 0; $i -lt $bitmaps.Count; $i++) {
        $ms.Position = $entryStart + $i * 16
        $sz = $bitmaps[$i].Width
        $w = if ($sz -ge 256) { 0 } else { $sz }
        $h = $w
        $bw.Write([byte]$w)
        $bw.Write([byte]$h)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$imageBlobs[$i].Length)
        $bw.Write([UInt32]$offsets[$i])
    }

    $bw.Flush()
    $out = Join-Path $Assets $Name
    [System.IO.File]::WriteAllBytes($out, $ms.ToArray())
    $bw.Dispose()
    $ms.Dispose()
    foreach ($b in $bitmaps) { $b.Dispose() }
    "  $Name (multi: $($Sizes -join ', '))"
}

"Generating vault icon assets in $Assets ..."
Save-Png  88  'Square44x44Logo.scale-200.png'
Save-Png  24  'Square44x44Logo.targetsize-24_altform-unplated.png' -Transparent $true
Save-Png  48  'Square44x44Logo.targetsize-48_altform-lightunplated.png' -Transparent $true
Save-Png 300  'Square150x150Logo.scale-200.png'
Save-Png  48  'LockScreenLogo.scale-200.png'
Save-Png  50  'StoreLogo.png'
Save-Wide 620 300  'Wide310x150Logo.scale-200.png'
Save-Wide 1240 600 'SplashScreen.scale-200.png'
Save-Ico @(16, 32, 48, 64, 128, 256) 'AppIcon.ico'
$SourceImage.Dispose()
"Done."
