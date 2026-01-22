Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon('C:\Windows\System32\cleanmgr.exe')
$stream = [System.IO.File]::Create('C:\git\lapriselemay_solution#1\TempCleaner\app.ico')
$icon.Save($stream)
$stream.Close()
$icon.Dispose()
Write-Host "Icon created successfully"
