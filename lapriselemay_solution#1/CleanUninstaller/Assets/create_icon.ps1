# Création d'une icône pour Clean Uninstaller
# Ce script crée une icône PNG qui peut être convertie en ICO

Add-Type -AssemblyName System.Drawing

$size = 256
$bitmap = New-Object System.Drawing.Bitmap($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

# Fond transparent
$graphics.Clear([System.Drawing.Color]::Transparent)

# Couleurs
$accentColor = [System.Drawing.Color]::FromArgb(255, 0, 120, 212)  # Bleu Windows
$dangerColor = [System.Drawing.Color]::FromArgb(255, 232, 17, 35)  # Rouge
$whiteColor = [System.Drawing.Color]::White

# Créer les pinceaux
$accentBrush = New-Object System.Drawing.SolidBrush($accentColor)
$dangerBrush = New-Object System.Drawing.SolidBrush($dangerColor)
$whiteBrush = New-Object System.Drawing.SolidBrush($whiteColor)
$whitePen = New-Object System.Drawing.Pen($whiteColor, 12)
$whitePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$whitePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

# Fond rond bleu
$graphics.FillEllipse($accentBrush, 10, 10, $size - 20, $size - 20)

# Icône de corbeille stylisée
# Corps de la corbeille
$bodyPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$bodyPath.AddPolygon(@(
    (New-Object System.Drawing.Point(75, 95)),
    (New-Object System.Drawing.Point(85, 195)),
    (New-Object System.Drawing.Point(171, 195)),
    (New-Object System.Drawing.Point(181, 95))
))
$graphics.FillPath($whiteBrush, $bodyPath)

# Couvercle
$graphics.FillRectangle($whiteBrush, 60, 75, 136, 18)

# Poignée du couvercle
$graphics.FillRectangle($whiteBrush, 108, 55, 40, 22)

# Lignes verticales sur la corbeille
$linePen = New-Object System.Drawing.Pen($accentColor, 6)
$graphics.DrawLine($linePen, 105, 110, 105, 180)
$graphics.DrawLine($linePen, 128, 110, 128, 180)
$graphics.DrawLine($linePen, 151, 110, 151, 180)

# X rouge en bas à droite (badge de suppression)
$badgeSize = 70
$badgeX = $size - $badgeSize - 15
$badgeY = $size - $badgeSize - 15
$graphics.FillEllipse($dangerBrush, $badgeX, $badgeY, $badgeSize, $badgeSize)

# X blanc dans le badge
$xPen = New-Object System.Drawing.Pen($whiteColor, 8)
$xPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$xPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$margin = 18
$graphics.DrawLine($xPen, $badgeX + $margin, $badgeY + $margin, $badgeX + $badgeSize - $margin, $badgeY + $badgeSize - $margin)
$graphics.DrawLine($xPen, $badgeX + $badgeSize - $margin, $badgeY + $margin, $badgeX + $margin, $badgeY + $badgeSize - $margin)

# Sauvegarder en PNG
$outputPath = Join-Path $PSScriptRoot "app.png"
$bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)

# Créer différentes tailles pour le ICO
$sizes = @(16, 32, 48, 64, 128, 256)
$tempFiles = @()

foreach ($s in $sizes) {
    $resized = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($resized)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($bitmap, 0, 0, $s, $s)
    
    $tempPath = Join-Path $PSScriptRoot "temp_$s.png"
    $resized.Save($tempPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $tempFiles += $tempPath
    
    $g.Dispose()
    $resized.Dispose()
}

# Nettoyage
$graphics.Dispose()
$bitmap.Dispose()
$accentBrush.Dispose()
$dangerBrush.Dispose()
$whiteBrush.Dispose()
$whitePen.Dispose()
$linePen.Dispose()
$xPen.Dispose()

Write-Host "Icône créée: $outputPath"
Write-Host "Utilisez un convertisseur en ligne pour créer le fichier .ico à partir des PNG générés"
Write-Host "Fichiers temporaires créés dans: $PSScriptRoot"
