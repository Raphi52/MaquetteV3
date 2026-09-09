Add-Type -AssemblyName System.Drawing
$sizes = 256,128,64,48,32,16
$pngs = @()
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap($s,$s,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.TextRenderingHint = 'AntiAliasGridFit'
  $r = $s/6.4
  $rect = New-Object System.Drawing.Rectangle(0,0,$s,$s)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r*2
  $path.AddArc(0,0,$d,$d,180,90)
  $path.AddArc($s-$d,0,$d,$d,270,90)
  $path.AddArc($s-$d,$s-$d,$d,$d,0,90)
  $path.AddArc(0,$s-$d,$d,$d,90,90)
  $path.CloseFigure()
  $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,12,15,22))
  $g.FillPath($bg,$path)
  # filet degrade haut (signature charte V1)
  $fh = [Math]::Max(2,[int]($s*0.09))
  $fr = New-Object System.Drawing.Rectangle([int]($s*0.18),[int]($s*0.20),[int]($s*0.64),$fh)
  $lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($fr,[System.Drawing.Color]::FromArgb(255,79,70,229),[System.Drawing.Color]::FromArgb(255,34,211,238),0.0)
  $fp = New-Object System.Drawing.Drawing2D.GraphicsPath
  $fd = $fh
  $fp.AddArc($fr.X,$fr.Y,$fd,$fd,90,180)
  $fp.AddArc($fr.Right-$fd,$fr.Y,$fd,$fd,270,180)
  $fp.CloseFigure()
  $g.FillPath($lg,$fp)
  # R
  $fs = $s*0.52
  $font = New-Object System.Drawing.Font('Segoe UI',$fs,[System.Drawing.FontStyle]::Bold,[System.Drawing.GraphicsUnit]::Pixel)
  $sf = New-Object System.Drawing.StringFormat
  $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
  $trect = New-Object System.Drawing.RectangleF(0,[single]($s*0.14),[single]$s,[single]$s)
  $wb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,242,245,250))
  $g.DrawString('R',$font,$wb,$trect,$sf)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += ,@($s,$ms.ToArray())
  $bmp.Dispose()
}
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
$offset = 6 + 16*$pngs.Count
foreach ($p in $pngs) {
  $sz = $p[0]; $data = $p[1]
  $bw.Write([byte]$(if($sz -ge 256){0}else{$sz}))
  $bw.Write([byte]$(if($sz -ge 256){0}else{$sz}))
  $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32)
  $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
  $offset += $data.Length
}
foreach ($p in $pngs) { $bw.Write($p[1]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes("$PSScriptRoot\wwwroot\rig.ico",$out.ToArray())
Write-Host "ico ecrit:" ((Get-Item "$PSScriptRoot\wwwroot\rig.ico").Length) "octets"
