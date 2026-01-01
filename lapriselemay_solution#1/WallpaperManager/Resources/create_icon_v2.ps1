Add-Type -AssemblyName System.Drawing

function Create-WallpaperIcon {
    param([int]$size)
    
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::Transparent)
    
    $margin = [int]($size * 0.08)
    $monitorRect = New-Object System.Drawing.Rectangle($margin, $margin, ($size - 2*$margin), ($size - 2*$margin))
    
    # Monitor frame
    $frameBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 45, 45, 65))
    $cornerRadius = [int]($size * 0.12)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($monitorRect.X, $monitorRect.Y, $cornerRadius, $cornerRadius, 180, 90)
    $path.AddArc($monitorRect.Right - $cornerRadius, $monitorRect.Y, $cornerRadius, $cornerRadius, 270, 90)
    $path.AddArc($monitorRect.Right - $cornerRadius, $monitorRect.Bottom - $cornerRadius, $cornerRadius, $cornerRadius, 0, 90)
    $path.AddArc($monitorRect.X, $monitorRect.Bottom - $cornerRadius, $cornerRadius, $cornerRadius, 90, 90)
    $path.CloseFigure()
    $g.FillPath($frameBrush, $path)
    
    # Screen with gradient
    $screenMargin = [int]($size * 0.15)
    $screenRect = New-Object System.Drawing.Rectangle($screenMargin, $screenMargin, ($size - 2*$screenMargin), ($size - 2*$screenMargin - [int]($size * 0.08)))
    
    $gradientBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $screenRect,
        [System.Drawing.Color]::FromArgb(255, 99, 102, 241),
        [System.Drawing.Color]::FromArgb(255, 59, 130, 246),
        45
    )
    
    $screenPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $screenCorner = [int]($size * 0.06)
    $screenPath.AddArc($screenRect.X, $screenRect.Y, $screenCorner, $screenCorner, 180, 90)
    $screenPath.AddArc($screenRect.Right - $screenCorner, $screenRect.Y, $screenCorner, $screenCorner, 270, 90)
    $screenPath.AddArc($screenRect.Right - $screenCorner, $screenRect.Bottom - $screenCorner, $screenCorner, $screenCorner, 0, 90)
    $screenPath.AddArc($screenRect.X, $screenRect.Bottom - $screenCorner, $screenCorner, $screenCorner, 90, 90)
    $screenPath.CloseFigure()
    $g.FillPath($gradientBrush, $screenPath)
    
    # Sun
    $sunSize = [int]($size * 0.18)
    $sunX = $screenRect.Right - $sunSize - [int]($size * 0.08)
    $sunY = $screenRect.Y + [int]($size * 0.06)
    $sunBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 251, 191, 36))
    $g.FillEllipse($sunBrush, $sunX, $sunY, $sunSize, $sunSize)
    
    # Mountains
    $mountainBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(200, 30, 64, 175))
    $baseY = $screenRect.Bottom - [int]($size * 0.04)
    $points1 = @(
        [System.Drawing.Point]::new($screenRect.X, $baseY),
        [System.Drawing.Point]::new([int]($screenRect.X + $size * 0.25), [int]($screenRect.Y + $size * 0.35)),
        [System.Drawing.Point]::new([int]($screenRect.X + $size * 0.4), $baseY)
    )
    $g.FillPolygon($mountainBrush, $points1)
    
    $mountain2Brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(220, 67, 56, 202))
    $points2 = @(
        [System.Drawing.Point]::new([int]($screenRect.X + $size * 0.2), $baseY),
        [System.Drawing.Point]::new([int]($screenRect.X + $size * 0.45), [int]($screenRect.Y + $size * 0.25)),
        [System.Drawing.Point]::new($screenRect.Right, $baseY)
    )
    $g.FillPolygon($mountain2Brush, $points2)
    
    # Stand
    $standWidth = [int]($size * 0.2)
    $standHeight = [int]($size * 0.08)
    $standX = [int](($size - $standWidth) / 2)
    $standY = $monitorRect.Bottom - [int]($size * 0.02)
    $standBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 55, 55, 75))
    $g.FillRectangle($standBrush, $standX, $standY, $standWidth, $standHeight)
    
    $g.Dispose()
    return $bmp
}

$outputPath = "C:\git\lapriselemay_solution#1\WallpaperManager\Resources\app.ico"
$sizes = @(16, 24, 32, 48, 64, 128, 256)

$memStream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($memStream)

# ICO Header
$writer.Write([Int16]0)
$writer.Write([Int16]1)
$writer.Write([Int16]$sizes.Count)

$dataOffset = 6 + ($sizes.Count * 16)
$imageDataList = New-Object System.Collections.ArrayList

foreach ($size in $sizes) {
    $bmp = Create-WallpaperIcon -size $size
    $pngStream = New-Object System.IO.MemoryStream
    $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngStream.ToArray()
    $pngStream.Dispose()
    $bmp.Dispose()
    
    [void]$imageDataList.Add($pngBytes)
    
    $w = if ($size -ge 256) { 0 } else { $size }
    $h = if ($size -ge 256) { 0 } else { $size }
    
    $writer.Write([byte]$w)
    $writer.Write([byte]$h)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([Int16]1)
    $writer.Write([Int16]32)
    $writer.Write([Int32]$pngBytes.Length)
    $writer.Write([Int32]$dataOffset)
    
    $dataOffset += $pngBytes.Length
}

foreach ($data in $imageDataList) {
    $writer.Write($data)
}

$writer.Flush()
[System.IO.File]::WriteAllBytes($outputPath, $memStream.ToArray())
$writer.Dispose()
$memStream.Dispose()

$info = Get-Item $outputPath
Write-Host "Icon created: $outputPath"
Write-Host "Size: $($info.Length) bytes"
