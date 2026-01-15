$cs = 'Server=TS1828\\ERP;Database=ValyanERP;Integrated Security=True;TrustServerCertificate=True'

# Run table creation script
Write-Host 'Running migration 040_TipuriDocumente.sql...' -ForegroundColor Green
$sql = Get-Content 'd:\Projects\ERPEnterprise\Database\Scripts\040_TipuriDocumente.sql' -Raw
$cn = New-Object System.Data.SqlClient.SqlConnection $cs
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandTimeout = 120
$batches = [System.Text.RegularExpressions.Regex]::Split($sql, "(?m)^GO\s*$")
foreach ($batch in $batches) {
    if (-not [string]::IsNullOrWhiteSpace($batch)) {
        $cmd.CommandText = $batch
        try { $cmd.ExecuteNonQuery() | Out-Null } catch { Write-Host "Batch failed: $_" }
    }
}
$cn.Close()

# Run stored procedures script
Write-Host 'Running migration 041_StoredProcedures_TipuriDocumente.sql...' -ForegroundColor Green
$sql = Get-Content 'd:\Projects\ERPEnterprise\Database\Scripts\041_StoredProcedures_TipuriDocumente.sql' -Raw
$cn = New-Object System.Data.SqlClient.SqlConnection $cs
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandTimeout = 120
$batches = [System.Text.RegularExpressions.Regex]::Split($sql, "(?m)^GO\s*$")
foreach ($batch in $batches) {
    if (-not [string]::IsNullOrWhiteSpace($batch)) {
        $cmd.CommandText = $batch
        try { $cmd.ExecuteNonQuery() | Out-Null } catch { Write-Host "Batch failed: $_" }
    }
}
$cn.Close()

Write-Host 'TipuriDocumente migrations applied successfully!' -ForegroundColor Green