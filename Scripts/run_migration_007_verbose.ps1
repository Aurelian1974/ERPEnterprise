param(
    [int]$Retries = 3,
    [int]$DelaySeconds = 5
)

# Read connection string from appsettings.json (ConnectionStrings:DefaultConnection)
$configPath = 'd:\Projects\ERPEnterprise\ValyanERP.Web\appsettings.json'
if (Test-Path $configPath) {
    try {
        $json = Get-Content $configPath -Raw | ConvertFrom-Json
        $cs = $json.ConnectionStrings.DefaultConnection
    }
    catch {
        Write-Host "Failed to read appsettings.json: $_" -ForegroundColor Yellow
        $cs = 'Server=TS1828\\ERP;Database=ValyanERP;Integrated Security=True;TrustServerCertificate=True'
    }
}
else {
    Write-Host "appsettings.json not found at $configPath - falling back to default connection string" -ForegroundColor Yellow
    $cs = 'Server=TS1828\\ERP;Database=ValyanERP;Integrated Security=True;TrustServerCertificate=True'
}

$sqlFile = 'd:\Projects\ERPEnterprise\Database\Scripts\007_SetCreatedAtDefaultsToBucharest.sql'

Write-Host "Migration script: $sqlFile"
Write-Host "Connection string: $cs"

$sql = Get-Content $sqlFile -Raw
$batches = [System.Text.RegularExpressions.Regex]::Split($sql, "(?m)^GO\s*$")

for ($attempt = 1; $attempt -le $Retries; $attempt++) {
    try {
        Write-Host ("Attempt {0}: opening connection..." -f $attempt)
        $cn = New-Object System.Data.SqlClient.SqlConnection $cs
        $cn.Open()
        Write-Host ("Connected. Server version: {0}" -f $cn.ServerVersion)

        $cmd = $cn.CreateCommand()
        $cmd.CommandTimeout = 600

        foreach ($batch in $batches) {
            if (-not [string]::IsNullOrWhiteSpace($batch)) {
                try {
                    Write-Host "Executing batch (first 120 chars): $($batch.Substring(0,[Math]::Min(120,$batch.Length))) ..."
                    $cmd.CommandText = $batch
                    $rows = $cmd.ExecuteNonQuery()
                    Write-Host "Batch executed. Affected rows (if applicable): $rows"
                }
                catch {
                    Write-Host "Batch failed: $_" -ForegroundColor Red
                    throw
                }
            }
        }

        Write-Host 'Migration 007 applied successfully.' -ForegroundColor Green
        $cn.Close()
        exit 0
    }
    catch {
        Write-Host "Attempt $attempt failed: $_" -ForegroundColor Yellow
        if ($attempt -lt $Retries) {
            Write-Host "Waiting $DelaySeconds seconds before retry..."
            Start-Sleep -Seconds $DelaySeconds
        }
    }
}

Write-Host 'All attempts failed. Please check SQL Server accessibility, credentials and network. You can run the script in SSMS if preferred.' -ForegroundColor Red
exit 1
