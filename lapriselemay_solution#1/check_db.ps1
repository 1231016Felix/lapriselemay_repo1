Add-Type -Path "C:\git\lapriselemay_solution#1\QuickLauncher\bin\Release\net9.0-windows\System.Data.SQLite.dll"
$conn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=C:\Users\melis\AppData\Roaming\QuickLauncher\index.db")
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT COUNT(*) FROM Items"
$count = $cmd.ExecuteScalar()
Write-Host "Nombre d'elements: $count"
$cmd.CommandText = "SELECT Name, Type FROM Items LIMIT 15"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) { 
    Write-Host "$($reader['Name']) - Type: $($reader['Type'])" 
}
$conn.Close()
