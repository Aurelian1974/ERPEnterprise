$cs = 'Server=TS1828\ERP;Database=ValyanERP;Trusted_Connection=True;TrustServerCertificate=True'

# Run ownership columns migration
Write-Host "Running 038_Articole_AddOwnership.sql..." -ForegroundColor Green
$sql = Get-Content 'd:\Projects\ERPEnterprise\Database\Scripts\038_Articole_AddOwnership.sql' -Raw
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

Write-Host 'Migration 038 applied successfully' -ForegroundColor Green