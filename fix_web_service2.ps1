$file = 'C:\git\lapriselemay_solution#1\QuickLauncher\Services\WebIntegrationService.cs'
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
$before = $lines[0..399]
$after = $lines[867..($lines.Length - 1)]
$result = $before + $after
[System.IO.File]::WriteAllLines($file, $result, [System.Text.Encoding]::UTF8)
Write-Output "Done. Lines before: $($lines.Length), after: $($result.Length)"
