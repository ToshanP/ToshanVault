# Renders the 🔐 emoji to PNGs at standard MSIX/Windows tile sizes and an
# AppIcon.ico containing the key sizes. Output replaces the placeholder
# WinUI assets so the title-bar and taskbar both show the lock glyph.
param(
    [string]$AssetsDir = "$PSScriptRoot\..\src\ToshanVault.App\Assets"
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Drawing

$emoji = [string][char]::ConvertFromUtf32(0x1F510)  # 🔐
$dpi   = 96.0

function Render-Emoji([int]$w, [int]$h, [string]$path, [string]$bg = $null) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()

    if ($bg) {
        $brush = (New-Object System.Windows.Media.BrushConverter).ConvertFromString($bg)
        $rect = New-Object System.Windows.Rect 0,0,$w,$h
        $dc.DrawRectangle($brush, $null, $rect)
    }

    # Sized so the glyph fills ~80 % of the smaller dimension; centred.
    $em      = [Math]::Min($w, $h) * 0.80
    $tf      = New-Object System.Windows.Media.Typeface "Segoe UI Emoji"
    $ft      = New-Object System.Windows.Media.FormattedText `
                  $emoji, ([System.Globalization.CultureInfo]::InvariantCulture),
                  ([System.Windows.FlowDirection]::LeftToRight),
                  $tf, $em, ([System.Windows.Media.Brushes]::Black), $dpi
    # Color-glyph rendering is implicit when the font has COLR/CPAL tables
    # (Segoe UI Emoji on Win10+); FormattedText picks the colour run.
    $x = ($w - $ft.Width)  / 2.0
    $y = ($h - $ft.Height) / 2.0
    $dc.DrawText($ft, (New-Object System.Windows.Point $x, $y))
    $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap `
              $w, $h, $dpi, $dpi, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    [void]$enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $fs = [System.IO.File]::Create($path)
    try { $enc.Save($fs) } finally { $fs.Close() }
}

$tmp = Join-Path $env:TEMP "tv-icons"
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
[void](New-Item -ItemType Directory -Path $tmp -Force)

# ICO source frames (transparent background).
$icoSizes = 16,20,24,32,40,48,64,96,128,256
foreach ($s in $icoSizes) { Render-Emoji $s $s (Join-Path $tmp "ico-$s.png") }

# Build a real multi-frame .ico file. ICONDIR (6) + ICONDIRENTRY (16 each) +
# embedded PNG bytes. PNG-in-ICO is supported by Windows for sizes >= 256
# but works fine for smaller ones too and avoids a BMP encoder.
$icoPath = Join-Path $AssetsDir "AppIcon.ico"
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([uint16]0)                  # reserved
$bw.Write([uint16]1)                  # type = icon
$bw.Write([uint16]$icoSizes.Length)   # image count

# Reserve directory entries; remember their offsets for back-patching.
$dirEntryOffset = $ms.Position
for ($i=0; $i -lt $icoSizes.Length; $i++) { $bw.Write([byte[]](,0 * 16)) }

# Append PNGs and patch each directory entry.
$entries = @()
foreach ($s in $icoSizes) {
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $tmp "ico-$s.png"))
    $offset = [int]$ms.Position
    $bw.Write($bytes)
    $entries += [pscustomobject]@{ Size=$s; Offset=$offset; Length=$bytes.Length }
}

# Patch directory entries.
for ($i=0; $i -lt $entries.Count; $i++) {
    $e = $entries[$i]
    $ms.Position = $dirEntryOffset + ($i * 16)
    $w = if ($e.Size -ge 256) { 0 } else { $e.Size }   # 0 means 256 in ICO
    $bw.Write([byte]$w)            # width
    $bw.Write([byte]$w)            # height
    $bw.Write([byte]0)             # palette
    $bw.Write([byte]0)             # reserved
    $bw.Write([uint16]1)           # planes
    $bw.Write([uint16]32)          # bpp
    $bw.Write([uint32]$e.Length)   # bytes in resource
    $bw.Write([uint32]$e.Offset)   # offset
}
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose()
Write-Host "Wrote $icoPath ($($ms.Length) bytes, $($icoSizes.Count) frames)"

# Tile/logo PNGs that the package manifest references. Sizes follow MSIX
# defaults (no Plated variants needed because we render solid backgrounds).
# Square 44x44 (taskbar / Start small)
Render-Emoji  44  44 (Join-Path $AssetsDir "Square44x44Logo.png")
Render-Emoji  88  88 (Join-Path $AssetsDir "Square44x44Logo.scale-200.png")
Render-Emoji  24  24 (Join-Path $AssetsDir "Square44x44Logo.targetsize-24_altform-unplated.png")
Render-Emoji  48  48 (Join-Path $AssetsDir "Square44x44Logo.targetsize-48_altform-lightunplated.png")
# Square 150x150 (Start medium)
Render-Emoji 150 150 (Join-Path $AssetsDir "Square150x150Logo.png")
Render-Emoji 300 300 (Join-Path $AssetsDir "Square150x150Logo.scale-200.png")
# Wide 310x150 — keep glyph centred on transparent background
Render-Emoji 310 150 (Join-Path $AssetsDir "Wide310x150Logo.png")
Render-Emoji 620 300 (Join-Path $AssetsDir "Wide310x150Logo.scale-200.png")
# Splash 620x300
Render-Emoji 620 300 (Join-Path $AssetsDir "SplashScreen.scale-200.png")
# Lock-screen / store
Render-Emoji  24  24 (Join-Path $AssetsDir "LockScreenLogo.scale-200.png")
Render-Emoji  50  50 (Join-Path $AssetsDir "StoreLogo.png")

Write-Host "Done. Wrote 11 PNGs + AppIcon.ico"
