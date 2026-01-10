$cs = 'Server=TS1828\ERP;Database=ValyanERP;Integrated Security=True;TrustServerCertificate=True'
$sql = Get-Content 'd:\Projects\ERPEnterprise\Database\Scripts\006_CreateSessions.sql' -Raw
$cn = New-Object System.Data.SqlClient.SqlConnection $cs
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandTimeout = 120

# Split on GO statements and run each batch
$batches = [System.Text.RegularExpressions.Regex]::Split($sql, "(?m)^GO\s*$")
foreach ($batch in $batches) {
    if (-not [string]::IsNullOrWhiteSpace($batch)) {
        $cmd.CommandText = $batch
        $cmd.ExecuteNonQuery() | Out-Null
    }
}

Write-Host 'Sessions table created or updated successfully'
$cn.Close()