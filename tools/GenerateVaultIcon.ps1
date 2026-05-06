# Generates a stylized "vault door" app icon at all sizes required by the
# WinUI 3 packaging manifest and writes them into ../src/ToshanVault.App/Assets.
#
# Design: dark navy rounded square background, brushed-gold circular vault
# door with a 4-spoke handle and small bolt rivets around the rim, plus a
# bright accent dot for the "lock" status light. Pure System.Drawing - no
# external deps. Re-run any time after tweaking colours/geometry.
#
# Usage: pwsh -File tools/GenerateVaultIcon.ps1

Add-Type -AssemblyName System.Drawing

$Assets = Join-Path $PSScriptRoot '..\src\ToshanVault.App\Assets'
$Assets = (Resolve-Path $Assets).Path

# Brand palette
$bgTop    = [System.Drawing.Color]::FromArgb(255, 18, 32, 56)    # deep navy
$bgBot    = [System.Drawing.Color]::FromArgb(255, 32, 54, 92)
$goldTop  = [System.Drawing.Color]::FromArgb(255, 245, 200, 90)
$goldBot  = [System.Drawing.Color]::FromArgb(255, 178, 130, 40)
$rimDark  = [System.Drawing.Color]::FromArgb(255, 110, 78, 24)
$accent   = [System.Drawing.Color]::FromArgb(255, 102, 220, 130)  # green = locked/secure
$shadow   = [System.Drawing.Color]::FromArgb(120, 0, 0, 0)

function New-VaultBitmap {
    param([int]$Size, [bool]$Transparent = $false)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    if (-not $Transparent) {
        $rect = New-Object System.Drawing.Rectangle 0, 0, $Size, $Size
        $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect, $bgTop, $bgBot, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
        $r = [int]($Size * 0.18)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc(0, 0, $r*2, $r*2, 180, 90)
        $path.AddArc($Size - $r*2, 0, $r*2, $r*2, 270, 90)
        $path.AddArc($Size - $r*2, $Size - $r*2, $r*2, $r*2, 0, 90)
        $path.AddArc(0, $Size - $r*2, $r*2, $r*2, 90, 90)
        $path.CloseFigure()
        $g.FillPath($bgBrush, $path)
        $bgBrush.Dispose()
        $path.Dispose()
    }

    $pad = [int]($Size * 0.14)
    $doorD = $Size - 2 * $pad
    $cx = $Size / 2.0
    $cy = $Size / 2.0

    $shadowRect = New-Object System.Drawing.Rectangle ($pad + [int]($Size*0.02)), ($pad + [int]($Size*0.04)), $doorD, $doorD
    $shBrush = New-Object System.Drawing.SolidBrush $shadow
    $g.FillEllipse($shBrush, $shadowRect)
    $shBrush.Dispose()

    $rimRect = New-Object System.Drawing.Rectangle $pad, $pad, $doorD, $doorD
    $rimBrush = New-Object System.Drawing.SolidBrush $rimDark
    $g.FillEllipse($rimBrush, $rimRect)
    $rimBrush.Dispose()

    $inset = [int]($Size * 0.035)
    $innerD = $doorD - 2 * $inset
    $innerRect = New-Object System.Drawing.Rectangle ($pad + $inset), ($pad + $inset), $innerD, $innerD
    $goldBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $innerRect, $goldTop, $goldBot, [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillEllipse($goldBrush, $innerRect)
    $goldBrush.Dispose()

    $rivetD = [Math]::Max(2, [int]($Size * 0.04))
    $rivetR = ($doorD / 2.0) - ($inset / 2.0)
    $rivetBrush = New-Object System.Drawing.SolidBrush $rimDark
    for ($i = 0; $i -lt 8; $i++) {
        $a = ($i * 45.0 + 22.5) * [Math]::PI / 180.0
        $rx = $cx + $rivetR * [Math]::Cos($a) - $rivetD / 2.0
        $ry = $cy + $rivetR * [Math]::Sin($a) - $rivetD / 2.0
        $g.FillEllipse($rivetBrush, [float]$rx, [float]$ry, [float]$rivetD, [float]$rivetD)
    }
    $rivetBrush.Dispose()

    $hubD = [int]($Size * 0.16)
    $hubRect = New-Object System.Drawing.Rectangle ([int]($cx - $hubD/2)), ([int]($cy - $hubD/2)), $hubD, $hubD
    $hubBrush = New-Object System.Drawing.SolidBrush $rimDark
    $g.FillEllipse($hubBrush, $hubRect)
    $hubBrush.Dispose()

    $spokeLen = [int]($innerD * 0.42)
    $spokeW = [Math]::Max(3, [int]($Size * 0.05))
    $spokePen = New-Object System.Drawing.Pen $rimDark, $spokeW
    $spokePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $spokePen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($deg in 45, 135, 225, 315) {
        $a = $deg * [Math]::PI / 180.0
        $x1 = $cx + ($hubD/2 - 1) * [Math]::Cos($a)
        $y1 = $cy + ($hubD/2 - 1) * [Math]::Sin($a)
        $x2 = $cx + $spokeLen * [Math]::Cos($a)
        $y2 = $cy + $spokeLen * [Math]::Sin($a)
        $g.DrawLine($spokePen, [float]$x1, [float]$y1, [float]$x2, [float]$y2)
        $knobD = [int]($Size * 0.055)
        $knobBrush = New-Object System.Drawing.SolidBrush $rimDark
        $g.FillEllipse($knobBrush, [float]($x2 - $knobD/2), [float]($y2 - $knobD/2), [float]$knobD, [float]$knobD)
        $knobBrush.Dispose()
    }
    $spokePen.Dispose()

    $capD = [int]($Size * 0.07)
    $capBrush = New-Object System.Drawing.SolidBrush $goldTop
    $g.FillEllipse($capBrush, [float]($cx - $capD/2), [float]($cy - $capD/2), [float]$capD, [float]$capD)
    $capBrush.Dispose()

    if ($Size -ge 64) {
        $lightD = [Math]::Max(3, [int]($Size * 0.05))
        $lx = $cx - $lightD / 2.0
        $ly = $pad + $inset + [int]($innerD * 0.13) - $lightD / 2.0
        $lightBrush = New-Object System.Drawing.SolidBrush $accent
        $g.FillEllipse($lightBrush, [float]$lx, [float]$ly, [float]$lightD, [float]$lightD)
        $lightBrush.Dispose()
    }

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
    $rect = New-Object System.Drawing.Rectangle 0, 0, $W, $H
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect, $bgTop, $bgBot, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillRectangle($bgBrush, $rect)
    $bgBrush.Dispose()
    $iconSize = [int]($H * 0.85)
    $icon = New-VaultBitmap -Size $iconSize -Transparent $true
    $g.DrawImage($icon, [int](($W - $iconSize) / 2), [int](($H - $iconSize) / 2), $iconSize, $iconSize)
    $icon.Dispose()
    if ($W -gt $H * 1.5) {
        $textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
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
"Done."
