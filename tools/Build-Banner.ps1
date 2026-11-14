$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Raster TV banner and review preview, matching the native vector rbrowser_icon.xml.
function Draw-RbrowserMark($graphics,[single]$x,[single]$y,[single]$size) {
 $saved=$graphics.Save();$graphics.TranslateTransform($x,$y);$graphics.ScaleTransform($size/108,$size/108)
 $ink=[System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#F3F2FF'))
 $rect=[System.Drawing.RectangleF]::new(3,3,102,102)
 $gradient=[System.Drawing.Drawing2D.LinearGradientBrush]::new($rect,[System.Drawing.ColorTranslator]::FromHtml('#7A84D9'),[System.Drawing.ColorTranslator]::FromHtml('#434F99'),55)
 $letter=[System.Drawing.Drawing2D.GraphicsPath]::new()
 try {
  $graphics.FillEllipse($gradient,$rect)
  $letter.StartFigure();$letter.AddLine(34,29,55,29);$letter.AddBezier(55,29,68,29,76,36,76,47);$letter.AddBezier(76,47,76,56,72,62,64,65)
  $letter.AddLines([System.Drawing.PointF[]]@([System.Drawing.PointF]::new(64,65),[System.Drawing.PointF]::new(79,82),[System.Drawing.PointF]::new(63,82),[System.Drawing.PointF]::new(49,66),[System.Drawing.PointF]::new(47,66),[System.Drawing.PointF]::new(47,82),[System.Drawing.PointF]::new(34,82)));$letter.CloseFigure()
  $letter.StartFigure();$letter.AddLine(47,41,47,54);$letter.AddLine(47,54,55,54);$letter.AddBezier(55,54,60,54,63,52,63,47.5);$letter.AddBezier(63,47.5,63,43,60,41,55,41);$letter.CloseFigure()
  $matrix=[System.Drawing.Drawing2D.Matrix]::new(1,0,0.10,1,-5,0);try{$letter.Transform($matrix)}finally{$matrix.Dispose()};$graphics.FillPath($ink,$letter)
 } finally { $letter.Dispose();$gradient.Dispose();$ink.Dispose();$graphics.Restore($saved) }
}
$bitmap=[System.Drawing.Bitmap]::new(320,180);$graphics=[System.Drawing.Graphics]::FromImage($bitmap)
$font=[System.Drawing.Font]::new('Segoe UI',27,[System.Drawing.FontStyle]::Regular,[System.Drawing.GraphicsUnit]::Pixel)
$foreground=[System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#EDF1F5'))
try {
 $graphics.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias;$graphics.TextRenderingHint=[System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
 $graphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#171B20'));Draw-RbrowserMark $graphics 36 43 94
 $graphics.DrawString('browser',$font,$foreground,140,70)
 $bitmap.Save((Join-Path $PSScriptRoot '../Rbrowser/Platforms/Android/Resources/drawable/rbrowser_tv_banner.png'),[System.Drawing.Imaging.ImageFormat]::Png)
} finally { $foreground.Dispose();$font.Dispose();$graphics.Dispose();$bitmap.Dispose() }
$preview=[System.Drawing.Bitmap]::new(512,512);$graphics=[System.Drawing.Graphics]::FromImage($preview)
try {
 $graphics.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias;$graphics.Clear([System.Drawing.Color]::Transparent);Draw-RbrowserMark $graphics 0 0 512
 New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot '../artifacts') | Out-Null
 $preview.Save((Join-Path $PSScriptRoot '../artifacts/rbrowser-icon-indigo.png'),[System.Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose();$preview.Dispose() }
