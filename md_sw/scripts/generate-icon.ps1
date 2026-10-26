$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '..\src\assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$bitmap = New-Object System.Drawing.Bitmap 256,256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$shape = New-Object System.Drawing.Drawing2D.GraphicsPath
$radius = 54
$shape.AddArc(0,0,$radius,$radius,180,90)
$shape.AddArc((256-$radius),0,$radius,$radius,270,90)
$shape.AddArc((256-$radius),(256-$radius),$radius,$radius,0,90)
$shape.AddArc(0,(256-$radius),$radius,$radius,90,90)
$shape.CloseFigure()
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(52,119,101))
$graphics.FillPath($brush,$shape)
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White),17
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawLine($pen,44,161,44,104)
$graphics.DrawBezier($pen,44,118,60,88,81,97,81,118)
$graphics.DrawLine($pen,81,118,81,161)
$graphics.DrawBezier($pen,81,118,98,88,120,97,120,118)
$graphics.DrawLine($pen,120,118,120,161)
$graphics.DrawLine($pen,186,93,186,162)
$graphics.DrawLine($pen,164,143,186,165)
$graphics.DrawLine($pen,186,165,208,143)
$bitmap.Save((Join-Path $assetDirectory 'icon.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$stream = New-Object System.IO.MemoryStream
$bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
$bytes = $stream.ToArray()
$writer = New-Object System.IO.BinaryWriter ([System.IO.File]::Create((Join-Path $assetDirectory 'icon.ico')))
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]1)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([uint16]1)
$writer.Write([uint16]32)
$writer.Write([uint32]$bytes.Length)
$writer.Write([uint32]22)
$writer.Write($bytes)
$writer.Dispose()
$stream.Dispose()
$pen.Dispose()
$brush.Dispose()
$shape.Dispose()
$graphics.Dispose()
$bitmap.Dispose()
