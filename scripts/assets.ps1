$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src/Dedsec.Monitor/Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$bg = [System.Drawing.ColorTranslator]::FromHtml('#101310')
$green = [System.Drawing.ColorTranslator]::FromHtml('#C2F970')
$white = [System.Drawing.ColorTranslator]::FromHtml('#EBF0E7')
$muted = [System.Drawing.ColorTranslator]::FromHtml('#8C9A89')
function Draw-Mark($graphics, $x, $y, $scale) {
    $pen = [System.Drawing.Pen]::new($green, 7 * $scale)
    $graphics.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($x + 14*$scale,$y + 6*$scale),
        [System.Drawing.PointF]::new($x,$y + 6*$scale),
        [System.Drawing.PointF]::new($x,$y + 60*$scale),
        [System.Drawing.PointF]::new($x + 14*$scale,$y + 60*$scale)))
    $graphics.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($x + 68*$scale,$y + 6*$scale),
        [System.Drawing.PointF]::new($x + 82*$scale,$y + 6*$scale),
        [System.Drawing.PointF]::new($x + 82*$scale,$y + 60*$scale),
        [System.Drawing.PointF]::new($x + 68*$scale,$y + 60*$scale)))
    $graphics.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($x + 26*$scale,$y + 12*$scale),
        [System.Drawing.PointF]::new($x + 43*$scale,$y + 12*$scale),
        [System.Drawing.PointF]::new($x + 56*$scale,$y + 25*$scale),
        [System.Drawing.PointF]::new($x + 56*$scale,$y + 42*$scale),
        [System.Drawing.PointF]::new($x + 43*$scale,$y + 54*$scale),
        [System.Drawing.PointF]::new($x + 26*$scale,$y + 54*$scale),
        [System.Drawing.PointF]::new($x + 26*$scale,$y + 12*$scale)))
    $pen.Dispose()
}
$image = [System.Drawing.Bitmap]::new(256,256)
$g = [System.Drawing.Graphics]::FromImage($image)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear($bg); Draw-Mark $g 46 57 2
$image.Save((Join-Path $assets 'dedsec.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$stream = [System.IO.File]::Create((Join-Path $assets 'dedsec.ico'))
$icon = [System.Drawing.Icon]::FromHandle($image.GetHicon()); $icon.Save($stream); $stream.Dispose(); $icon.Dispose(); $g.Dispose(); $image.Dispose()
$banner = [System.Drawing.Bitmap]::new(1600,520)
$g = [System.Drawing.Graphics]::FromImage($banner)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear($bg)
$gridPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#20291E'),1)
for ($x=0; $x -lt 1600; $x+=40) { $g.DrawLine($gridPen,$x,0,$x,520) }
for ($y=0; $y -lt 520; $y+=40) { $g.DrawLine($gridPen,0,$y,1600,$y) }
Draw-Mark $g 100 92 2.2
$font = [System.Drawing.Font]::new('Segoe UI',64,[System.Drawing.FontStyle]::Bold)
$g.DrawString('DEDSEC MONITOR',$font,[System.Drawing.SolidBrush]::new($white),320,85)
$small = [System.Drawing.Font]::new('Consolas',18)
$g.DrawString('EVERY FRAME. UNDER CONTROL.',$small,[System.Drawing.SolidBrush]::new($green),329,201)
$g.DrawString('FPS  /  CPU  /  GPU  /  RAM',$small,[System.Drawing.SolidBrush]::new($muted),100,365)
$g.DrawString('OPEN SOURCE   +   LOCAL FIRST',$small,[System.Drawing.SolidBrush]::new($muted),100,410)
$points = [System.Drawing.PointF[]]@((900,400),(955,400),(973,350),(992,420),(1015,310),(1037,385),(1075,385),(1090,370),(1140,370),(1160,260),(1180,420),(1200,330),(1230,380),(1290,380),(1320,340),(1360,370),(1440,370),(1490,310) | ForEach-Object { [System.Drawing.PointF]::new($_[0],$_[1]) })
$g.DrawLines([System.Drawing.Pen]::new($green,3),$points)
$banner.Save((Join-Path $root 'docs/banner.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $banner.Dispose()
$wizard = [System.Drawing.Bitmap]::new(328,628)
$g = [System.Drawing.Graphics]::FromImage($wizard); $g.Clear($bg)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
Draw-Mark $g 55 70 2.5
$font = [System.Drawing.Font]::new('Segoe UI',29,[System.Drawing.FontStyle]::Bold)
$g.DrawString('DEDSEC',$font,[System.Drawing.SolidBrush]::new($white),37,270)
$g.DrawString('MONITOR',$font,[System.Drawing.SolidBrush]::new($white),37,322)
$g.DrawString('EVERY FRAME.',[System.Drawing.Font]::new('Consolas',13),[System.Drawing.SolidBrush]::new($green),43,470)
$g.DrawString('UNDER CONTROL.',[System.Drawing.Font]::new('Consolas',13),[System.Drawing.SolidBrush]::new($green),43,499)
$wizard.Save((Join-Path $root 'installer/wizard.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $wizard.Dispose()
