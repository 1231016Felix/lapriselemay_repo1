# Création du fichier ICO à partir du PNG
Add-Type -AssemblyName System.Drawing

$pngPath = Join-Path $PSScriptRoot "app.png"
$icoPath = Join-Path $PSScriptRoot "app.ico"

if (Test-Path $pngPath) {
    $png = [System.Drawing.Image]::FromFile($pngPath)
    $bitmap = [System.Drawing.Bitmap]$png
    $icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
    
    $stream = [System.IO.File]::Create($icoPath)
    $icon.Save($stream)
    $stream.Close()
    
    $icon.Dispose()
    $bitmap.Dispose()
    $png.Dispose()
    
    Write-Host "Icône ICO créée: $icoPath"
} else {
    Write-Host "Fichier PNG non trouvé: $pngPath"
}
