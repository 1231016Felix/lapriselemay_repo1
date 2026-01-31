$files = Get-ChildItem "C:\git\lapriselemay_solution#1\WallpaperManager\ViewModels\*.cs"

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    
    # Pattern pour les assignations simples: StatusMessage = "texte";
    $content = $content -replace 'StatusMessage = (".*?");', 'ShowStatusMessage($1);'
    
    # Pattern pour les assignations avec interpolation: StatusMessage = $"texte";
    $content = $content -replace 'StatusMessage = (\$".*?");', 'ShowStatusMessage($1);'
    
    Set-Content $file.FullName -Value $content -NoNewline
    Write-Host "Processed: $($file.Name)"
}

Write-Host "Done!"
