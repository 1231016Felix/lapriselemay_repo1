$shell = New-Object -ComObject Shell.Application
$folder = $shell.NameSpace('shell:AppsFolder')
$items = $folder.Items()

Write-Host "=== Apps matching 'Visual Studio' (excluding Installer/Code) ===" -ForegroundColor Cyan
foreach($item in $items) {
    if($item.Name -like '*Visual Studio*' -and $item.Name -notlike '*Installer*' -and $item.Name -notlike '*Code*') {
        Write-Host "Name: $($item.Name)"
        Write-Host "Path: $($item.Path)"
        Write-Host ""
    }
}

Write-Host "=== Apps matching 'Services' ===" -ForegroundColor Cyan
foreach($item in $items) {
    if($item.Name -eq 'Services' -or $item.Name -like 'Services de*') {
        Write-Host "Name: $($item.Name)"
        Write-Host "Path: $($item.Path)"
        Write-Host ""
    }
}
