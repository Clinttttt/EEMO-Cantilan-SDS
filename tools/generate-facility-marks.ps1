# Generates the web-sized facility marks used by the portal from the source artwork in
# wwwroot/images/facilities_logo (1024x1536, ~2 MB each, white line art on transparency).
#
# Why this exists: the source files are far too heavy to ship as 20px navigation icons, and their ink is
# white, so they are invisible on the portal's white cards. This produces, per facility code:
#   <code>-white.png   ink #ffffff  — for dark surfaces (navy sidebar, facility hero)
#   <code>-navy.png    ink #0d2137  — for light surfaces (dashboard cards, white panels)
# each cropped to the artwork's bounding box and fitted into a square canvas at 2x the largest on-screen
# size, so it stays crisp on HiDPI screens while costing a few KB.
#
# Re-run after replacing any source file:
#   pwsh -File tools/generate-facility-marks.ps1
#
# The 1024x1536 source artwork is deliberately NOT committed (12 MB); only the generated marks are, so a
# clone and the container image stay small. Keep the originals with the office's design assets and drop them
# back into the source folder when they need regenerating.

param(
    [string]$SourceDir = "EEMOCantilanSDS.Client/wwwroot/images/facilities_logo",
    # Both presentation apps get the same files: the portal and the collector app have separate wwwroots, so
    # writing both here is what keeps their artwork identical.
    [string[]]$OutputDirs = @(
        "EEMOCantilanSDS.Client/wwwroot/images/facility-marks",
        "EEMOCantilanSDS.Mobile/wwwroot/images/facility-marks"
    ),
    [int]$Size = 96
)

Add-Type -AssemblyName System.Drawing

# Facility code -> source artwork. Tabo-an deliberately reuses the public-market mark (it is the same kind
# of facility, collected weekly), and the two commercial centres share one.
$map = @{
    'npm' = 'public_market.png'
    'tcc' = 'commercial_center_logo.png'
    'ncc' = 'commercial_center_logo.png'
    'bbq' = 'barbeque_logo.png'
    'ice' = 'ice_plant_logo.png'
    'slh' = 'slaughter_house_logo.png'
    'trm' = 'transport_terminal.png'
    'tpm' = 'public_market.png'
}

$inks = @{
    'white' = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
    'navy'  = [System.Drawing.Color]::FromArgb(255, 13, 33, 55)     # --navy from app.css
}

foreach ($dir in $OutputDirs) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
}

foreach ($code in $map.Keys | Sort-Object) {
    $sourcePath = Join-Path $SourceDir $map[$code]
    if (-not (Test-Path $sourcePath)) { Write-Warning "missing source: $sourcePath"; continue }

    $src = New-Object System.Drawing.Bitmap($sourcePath)

    # Crop to the artwork: the source has wide transparent margins (portrait canvas), which would otherwise
    # shrink the glyph to nothing once fitted into a square.
    $minX = $src.Width; $minY = $src.Height; $maxX = -1; $maxY = -1
    for ($x = 0; $x -lt $src.Width; $x++) {
        for ($y = 0; $y -lt $src.Height; $y++) {
            if ($src.GetPixel($x, $y).A -gt 16) {
                if ($x -lt $minX) { $minX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { Write-Warning "$code : fully transparent source"; $src.Dispose(); continue }

    $cropW = $maxX - $minX + 1
    $cropH = $maxY - $minY + 1

    foreach ($inkName in $inks.Keys) {
        $ink = $inks[$inkName]

        # Square canvas, artwork centred and scaled to fit with a small breathing margin.
        $canvas = New-Object System.Drawing.Bitmap($Size, $Size)
        $g = [System.Drawing.Graphics]::FromImage($canvas)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        $margin = [int]($Size * 0.06)
        $box = $Size - 2 * $margin
        $scale = [Math]::Min($box / $cropW, $box / $cropH)
        $drawW = [int]($cropW * $scale)
        $drawH = [int]($cropH * $scale)
        $destRect = New-Object System.Drawing.Rectangle(
            [int](($Size - $drawW) / 2), [int](($Size - $drawH) / 2), $drawW, $drawH)
        $srcRect = New-Object System.Drawing.Rectangle($minX, $minY, $cropW, $cropH)
        $g.DrawImage($src, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()

        # Recolour the ink, keeping the anti-aliased alpha so edges stay smooth on both surfaces.
        for ($x = 0; $x -lt $Size; $x++) {
            for ($y = 0; $y -lt $Size; $y++) {
                $a = $canvas.GetPixel($x, $y).A
                if ($a -gt 0) {
                    $canvas.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($a, $ink.R, $ink.G, $ink.B))
                }
            }
        }

        foreach ($dir in $OutputDirs) {
            $outPath = Join-Path $dir "$code-$inkName.png"
            $canvas.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
            $kb = [Math]::Round((Get-Item $outPath).Length / 1KB, 1)
            Write-Host "$dir/$code-$inkName.png  ${Size}x${Size}  $kb KB"
        }
        $canvas.Dispose()
    }

    $src.Dispose()
}
