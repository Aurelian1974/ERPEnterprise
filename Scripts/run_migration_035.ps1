$cs = 'Server=TS1828\\ERP;Database=ValyanERP;Integrated Security=True;TrustServerCertificate=True'
$sql = Get-Content 'd:\Projects\ERPEnterprise\Database\Scripts\035_StoredProcedures_TipuriArticole.sql' -Raw
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
Write-Host 'Migration 035 StoredProcedures TipuriArticole applied (attempted)'
$cn.Close()