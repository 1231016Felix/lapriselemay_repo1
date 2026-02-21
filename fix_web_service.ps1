# Read the file
$file = 'C:\git\lapriselemay_solution#1\QuickLauncher\Services\WebIntegrationService.cs'
$lines = Get-Content $file -Encoding UTF8

# Line 401 is the broken "public void Dispose()" (0-indexed: 400)
# Lines 402-867 are dictionary code + #endregion (0-indexed: 401-866)
# Line 868 is blank, line 869 is the real "public void Dispose()" (0-indexed: 867, 868)

# Keep lines 0-399 (up to and including #endregion of Translation)
# Skip lines 400-866 (broken Dispose line + all dictionary code + dictionary #endregion)
# Keep lines 867+ (blank + real Dispose + closing brace + models)

$before = $lines[0..399]
$after = $lines[867..($lines.Length - 1)]

$result = $before + $after
$result | Set-Content $file -Encoding UTF8
Write-Output "Done. Lines before: $($lines.Length), after: $($result.Length)"
