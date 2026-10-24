# Builds thunderstore/icon.png (256x256): a burst like Good Cards' icon, a winding road painted asphalt, ice and
# magma, and the mod's name.
# Usage: powershell -ExecutionPolicy Bypass -File tools\make_icon.ps1
param([string]$Repo = (Split-Path -Parent $PSScriptRoot))
Add-Type -AssemblyName System.Drawing
function C([string]$hex, [int]$alpha = 255) { [System.Drawing.Color]::FromArgb($alpha, [System.Drawing.ColorTranslator]::FromHtml($hex)) }
function P([float]$x, [float]$y) { New-Object System.Drawing.PointF $x, $y }

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = "AntiAlias"
$g.TextRenderingHint = "AntiAliasGridFit"
$g.Clear((C "#0B7F8C"))

# Burst and glow.
$cx, $cy = 128, 150
for ($i = 0; $i -lt 24; $i += 2) {
    $a0 = 2 * [math]::PI * $i / 24; $a1 = 2 * [math]::PI * ($i + 1) / 24
    $pts = [System.Drawing.PointF[]]@((P $cx $cy), (P ($cx + [math]::Cos($a0) * 400) ($cy + [math]::Sin($a0) * 400)),
        (P ($cx + [math]::Cos($a1) * 400) ($cy + [math]::Sin($a1) * 400)))
    $brush = New-Object System.Drawing.SolidBrush (C "#22B5C2"); $g.FillPolygon($brush, $pts); $brush.Dispose()
}
$glow = New-Object System.Drawing.Drawing2D.GraphicsPath
$glow.AddEllipse($cx - 130, $cy - 130, 260, 260)
$glowBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow
$glowBrush.CenterColor = C "#FFF6D8" 200
$glowBrush.SurroundColors = [System.Drawing.Color[]]@((C "#FFF6D8" 0))
$g.FillPath($glowBrush, $glow); $glowBrush.Dispose(); $glow.Dispose()

# The road: one S-curve, flattened to a polyline so stretches of it can take different surfaces.
$curve = New-Object System.Drawing.Drawing2D.GraphicsPath
$curve.AddBezier((P -30 238), (P 70 112), (P 175 270), (P 290 140))
$curve.Flatten($null, 0.5)
$points = $curve.PathPoints
$curve.Dispose()
function Stroke($pts, [System.Drawing.Color]$color, [float]$width, [float]$dx = 0, [float]$dy = 0, [bool]$dashed = $false) {
    $pen = New-Object System.Drawing.Pen $color, $width
    $pen.StartCap = "Round"; $pen.EndCap = "Round"; $pen.LineJoin = "Round"
    if ($dashed) { $pen.DashPattern = [float[]]@(2.2, 2.2) }
    $shifted = [System.Drawing.PointF[]]($pts | ForEach-Object { P ($_.X + $dx) ($_.Y + $dy) })
    $g.DrawLines($pen, $shifted); $pen.Dispose()
}
$n = $points.Length
$third = [int]($n / 3)
Stroke $points (C "#10324A" 110) 58 5 9                          # shadow
Stroke $points (C "#E9ECFA") 56                                   # kerbs
Stroke $points[0..$third] (C "#3C4070") 44                        # asphalt
Stroke $points[$third..(2 * $third)] (C "#8BE6FF") 44             # ice
Stroke $points[(2 * $third)..($n - 1)] (C "#FF6B1C") 44           # magma
Stroke $points[0..$third] (C "#FFFFFF" 230) 4 0 0 $true           # centre dashes on the asphalt
$sparkle = New-Object System.Drawing.SolidBrush (C "#FFFFFF" 220)
foreach ($k in 0.2, 0.45, 0.7) {
    $p = $points[[int]($third + $k * $third)]
    $g.FillEllipse($sparkle, $p.X - 3, $p.Y - 3, 6, 6)
}
$sparkle.Dispose()
$ember = New-Object System.Drawing.SolidBrush (C "#FFD23A" 230)
foreach ($k in 0.15, 0.5, 0.8) {
    $p = $points[[int](2 * $third + $k * $third)]
    $g.FillEllipse($ember, $p.X - 4, $p.Y - 4, 8, 8)
}
$ember.Dispose()

# The name, white with a dark outline.
$family = @("Arial Black", "Segoe UI Black", "Impact", "Arial") | Where-Object {
    (New-Object System.Drawing.FontFamily $_ -ErrorAction SilentlyContinue) -ne $null } | Select-Object -First 1
$text = New-Object System.Drawing.Drawing2D.GraphicsPath
$format = New-Object System.Drawing.StringFormat
$format.Alignment = "Center"
$font = New-Object System.Drawing.FontFamily $family
$text.AddString("MORE", $font, [int][System.Drawing.FontStyle]::Bold, 58, (New-Object System.Drawing.RectangleF 0, 4, $size, 70), $format)
$text.AddString("MAPS", $font, [int][System.Drawing.FontStyle]::Bold, 58, (New-Object System.Drawing.RectangleF 0, 62, $size, 70), $format)
$outline = New-Object System.Drawing.Pen (C "#14233F"), 10
$outline.LineJoin = "Round"
$g.DrawPath($outline, $text)
$fill = New-Object System.Drawing.SolidBrush (C "#FFFFFF")
$g.FillPath($fill, $text)
$outline.Dispose(); $fill.Dispose(); $text.Dispose(); $format.Dispose(); $font.Dispose()

$g.Dispose()
$out = Join-Path $Repo "thunderstore\icon.png"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"wrote $out ($family)"
