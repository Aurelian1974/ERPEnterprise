$cs = 'Server=(localdb)\MSSQLLocalDB;Database=ValyanERP;Integrated Security=True;TrustServerCertificate=True'

# Run table creation first
Write-Host "Running 034_TipuriArticole.sql (table creation)..." -ForegroundColor Green
$sql034 = Get-Content 'd:\Projects\ERPEnterprise\Database\Scripts\034_TipuriArticole.sql' -Raw
$cn = New-Object System.Data.SqlClient.SqlConnection $cs
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandTimeout = 120
$batches034 = [System.Text.RegularExpressions.Regex]::Split($sql034, "(?m)^GO\s*$")
foreach ($batch in $batches034) {
    if (-not [string]::IsNullOrWhiteSpace($batch)) {
        $cmd.CommandText = $batch
        try { $cmd.ExecuteNonQuery() | Out-Null } catch { Write-Host "Batch failed: $_" }
    }
}
$cn.Close()

# Run stored procedures
Write-Host "Running 035_StoredProcedures_TipuriArticole.sql (stored procedures)..." -ForegroundColor Green
$sql035 = Get-Content 'd:\Projects\ERPEnterprise\Database\Scripts\035_StoredProcedures_TipuriArticole.sql' -Raw
$cn = New-Object System.Data.SqlClient.SqlConnection $cs
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandTimeout = 120
$batches035 = [System.Text.RegularExpressions.Regex]::Split($sql035, "(?m)^GO\s*$")
foreach ($batch in $batches035) {
    if (-not [string]::IsNullOrWhiteSpace($batch)) {
        $cmd.CommandText = $batch
        try { $cmd.ExecuteNonQuery() | Out-Null } catch { Write-Host "Batch failed: $_" }
    }
}
$cn.Close()

Write-Host 'Migrations 034 and 035 applied successfully' -ForegroundColor Green