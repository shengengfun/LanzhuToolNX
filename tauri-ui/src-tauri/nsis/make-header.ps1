# 生成 NSIS 安装向导顶栏图（150x57，24 位 BMP），内容就是 app logo。
# 换 logo 后重新跑一次即可：  powershell -ExecutionPolicy Bypass -File make-header.ps1
#
# NSIS 的 MUI_HEADERIMAGE_BITMAP 只吃 BMP，尺寸必须是 150x57，
# 背景要和 MUI 顶栏底色一致（默认纯白），所以这里先铺白底再贴图标，
# 并给图标加一点圆角，免得方角在白色顶栏上显得毛糙。

Add-Type -AssemblyName System.Drawing

$W = 150; $H = 57          # 顶栏尺寸，NSIS 规定
$PAD = 8                   # 右边距
$ICON = 44                 # 图标边长（正方形）
$RADIUS = 9                # 圆角半径

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)   # src-tauri
$src = Join-Path $root 'icons\icon.png'
$dst = Join-Path $root 'icons\nsis-header.bmp'

if (-not (Test-Path $src)) { throw "找不到 app 图标：$src" }

$source = [System.Drawing.Image]::FromFile($src)
$bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::White)

    $x = $W - $PAD - $ICON
    $y = [int](($H - $ICON) / 2)

    # 圆角矩形裁剪路径
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $RADIUS * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $ICON - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $ICON - $d, $y + $ICON - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $ICON - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $g.SetClip($path)
    $g.DrawImage($source, (New-Object System.Drawing.Rectangle($x, $y, $ICON, $ICON)))
    $g.ResetClip()
    $path.Dispose()

    $bmp.Save($dst, [System.Drawing.Imaging.ImageFormat]::Bmp)
} finally {
    $g.Dispose(); $bmp.Dispose(); $source.Dispose()
}

"wrote $dst ($((Get-Item $dst).Length) bytes)"
