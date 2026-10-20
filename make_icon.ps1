Add-Type -AssemblyName System.Drawing
$S = 256
$bmp = New-Object System.Drawing.Bitmap $S, $S
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)

function RR($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

# background: dark with hot stage glow
$bg = RR 6 6 244 244 52
$br = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 0, 256), [System.Drawing.Color]::FromArgb(255, 40, 8, 16), [System.Drawing.Color]::FromArgb(255, 8, 8, 14))
$g.FillPath($br, $bg)
$g.SetClip($bg)
$glow = New-Object System.Drawing.Drawing2D.GraphicsPath
$glow.AddEllipse(-40, 120, 336, 260)
$pg = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow
$pg.CenterColor = [System.Drawing.Color]::FromArgb(230, 255, 90, 20)
$pg.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 255, 90, 20))
$g.FillPath($pg, $glow)

# highway in perspective
$hw = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 108, 40), (New-Object System.Drawing.PointF 148, 40),
    (New-Object System.Drawing.PointF 236, 236), (New-Object System.Drawing.PointF 20, 236))
$hb = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.Point 0, 40), (New-Object System.Drawing.Point 0, 236), [System.Drawing.Color]::FromArgb(255, 20, 20, 30), [System.Drawing.Color]::FromArgb(255, 48, 48, 66))
$g.FillPolygon($hb, $hw)
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(160, 200, 200, 220)), 2
for ($i = 0; $i -le 5; $i++) {
    $t = $i / 5.0
    $xt = 108 + 40 * $t; $xb = 20 + 216 * $t
    $g.DrawLine($pen, $xt, 40, $xb, 236)
}
# beat lines
foreach ($v in 0.25, 0.5, 0.78) {
    $y = 40 + 196 * $v * $v
    $xl = 108 + (20 - 108) * (($y - 40) / 196); $xr = 148 + (236 - 148) * (($y - 40) / 196)
    $g.DrawLine($pen, $xl, $y, $xr, $y)
}

# gems: green red yellow blue orange
$cols = @(
    @(60, 220, 90), @(235, 50, 60), @(255, 215, 40), @(60, 140, 255), @(255, 140, 30))
$rows = @(@(0.40, 0, 2), @(0.62, 3, 4), @(0.84, 1, 2))
foreach ($rw in $rows) {
    $v = $rw[0]
    $y = 40 + 196 * $v
    $k = ($y - 40) / 196
    $xl = 108 + (20 - 108) * $k; $xr = 148 + (236 - 148) * $k
    $lw = ($xr - $xl) / 5
    $sz = 10 + 26 * $k
    foreach ($ln in @($rw[1], $rw[2])) {
        $cx = $xl + $lw * ($ln + 0.5)
        $c = $cols[$ln]
        $col = [System.Drawing.Color]::FromArgb(255, $c[0], $c[1], $c[2])
        $gb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, $c[0], $c[1], $c[2]))
        $g.FillEllipse($gb, $cx - $sz * 0.95, $y - $sz * 0.62, $sz * 1.9, $sz * 1.24)
        $fb = New-Object System.Drawing.SolidBrush $col
        $g.FillEllipse($fb, $cx - $sz * 0.7, $y - $sz * 0.38, $sz * 1.4, $sz * 0.76)
        $hl = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(170, 255, 255, 255))
        $g.FillEllipse($hl, $cx - $sz * 0.42, $y - $sz * 0.28, $sz * 0.8, $sz * 0.28)
    }
}

# strum bar / fret line
$fr = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)), 4
$g.DrawLine($fr, 26, 214, 230, 214)

# lightning bolt across the top
$bolt = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 140, 6), (New-Object System.Drawing.PointF 98, 78), (New-Object System.Drawing.PointF 126, 78),
    (New-Object System.Drawing.PointF 108, 140), (New-Object System.Drawing.PointF 170, 58), (New-Object System.Drawing.PointF 138, 58),
    (New-Object System.Drawing.PointF 164, 6))
$bb = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.Point 0, 6), (New-Object System.Drawing.Point 0, 140), [System.Drawing.Color]::FromArgb(255, 255, 240, 120), [System.Drawing.Color]::FromArgb(255, 255, 110, 20))
$g.FillPolygon($bb, $bolt)
$bp = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 20, 6, 6)), 4
$bp.LineJoin = 'Round'
$g.DrawPolygon($bp, $bolt)

# customization badge: paint palette, bottom-right
$pal = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 245, 235, 215))
$g.FillEllipse($pal, 140, 140, 104, 100)
$pp = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 20, 6, 6)), 5
$g.DrawEllipse($pp, 140, 140, 104, 100)
$hole = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 30, 10, 14))
$g.FillEllipse($hole, 200, 200, 26, 22)
$dots = @(@(158,160,235,50,60), @(184,152,255,215,40), @(210,160,60,220,90), @(152,188,60,140,255), @(160,214,190,80,230))
foreach ($d in $dots) {
    $db = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $d[2], $d[3], $d[4]))
    $g.FillEllipse($db, $d[0], $d[1], 22, 22)
}

$g.ResetClip()
$edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 110, 30)), 6
$g.DrawPath($edge, $bg)

# write multi-size ICO with PNG frames
$sizes = 256, 128, 64, 48, 32, 16
$frames = @()
foreach ($s in $sizes) {
    $b2 = New-Object System.Drawing.Bitmap $s, $s
    $g2 = [System.Drawing.Graphics]::FromImage($b2)
    $g2.InterpolationMode = 'HighQualityBicubic'; $g2.SmoothingMode = 'AntiAlias'
    $g2.DrawImage($bmp, 0, 0, $s, $s); $g2.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $b2.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , $ms.ToArray(); $b2.Dispose()
}
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$off = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $wb = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$wb); $bw.Write([byte]$wb); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$frames[$i].Length); $bw.Write([uint32]$off)
    $off += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write($f) }
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'songtool.ico'), $out.ToArray())
$bmp.Save((Join-Path $PSScriptRoot 'songtool_preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
"ok"
