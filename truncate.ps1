$lines = Get-Content 'C:\git\lapriselemay_solution#1\QuickLauncher\Services\WebIntegrationService.cs'
$keep = $lines[0..498]
$tempFile = 'C:\git\lapriselemay_solution#1\QuickLauncher\Services\WebIntegrationService.cs.tmp'
$keep | Set-Content $tempFile -Encoding UTF8
# Try to copy over the original
try {
    [System.IO.File]::WriteAllText('C:\git\lapriselemay_solution#1\QuickLauncher\Services\WebIntegrationService.cs', (Get-Content $tempFile -Raw), [System.Text.Encoding]::UTF8)
    Remove-Item $tempFile
    Write-Output "Done. Wrote $($keep.Count) lines."
} catch {
    Write-Output "File locked. Temp file at: $tempFile"
    Write-Output "Error: $_"
}
