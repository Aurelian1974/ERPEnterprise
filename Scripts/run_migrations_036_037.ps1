$cs = 'Server=TS1828\ERP;Database=ValyanERP;Trusted_Connection=True;TrustServerCertificate=True'

# Run table creation first
Write-Host "Running 036_Articole.sql (table creation)..." -ForegroundColor Green
$sql036 = Get-Content 'd:\Projects\ERPEnterprise\Database\Scripts\036_Articole.sql' -Raw
$cn = New-Object System.Data.SqlClient.SqlConnection $cs
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandTimeout = 120
$batches036 = [System.Text.RegularExpressions.Regex]::Split($sql036, "(?m)^GO\s*$")
foreach ($batch in $batches036) {
    if (-not [string]::IsNullOrWhiteSpace($batch)) {
        $cmd.CommandText = $batch
        try { $cmd.ExecuteNonQuery() | Out-Null } catch { Write-Host "Batch failed: $_" }
    }
}
$cn.Close()

# Run stored procedures
Write-Host "Running 037_StoredProcedures_Articole.sql (stored procedures)..." -ForegroundColor Green
$sql037 = Get-Content 'd:\Projects\ERPEnterprise\Database\Scripts\037_StoredProcedures_Articole.sql' -Raw
$cn = New-Object System.Data.SqlClient.SqlConnection $cs
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandTimeout = 120
$batches037 = [System.Text.RegularExpressions.Regex]::Split($sql037, "(?m)^GO\s*$")
foreach ($batch in $batches037) {
    if (-not [string]::IsNullOrWhiteSpace($batch)) {
        $cmd.CommandText = $batch
        try { $cmd.ExecuteNonQuery() | Out-Null } catch { Write-Host "Batch failed: $_" }
    }
}
$cn.Close()

Write-Host 'Migrations 036 and 037 applied successfully' -ForegroundColor Green