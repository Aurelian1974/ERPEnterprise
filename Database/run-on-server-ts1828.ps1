param(
    [string]$Server = 'TS1828\ERP',
    [string]$Database = 'ValyanERP'
)

Write-Host "Using server: $Server" -ForegroundColor Cyan

# Ensure database exists
Write-Host "Ensuring database $Database exists..."
Invoke-Sqlcmd -ServerInstance $Server -Query "IF DB_ID('$Database') IS NULL CREATE DATABASE [$Database];"

# Run scripts in order
$base = 'D:\Projects\ERPEnterprise\Database\Scripts'
$files = @(
    '001_CreateDatabase_noFK.sql',
    '003_Persoane_noFK.sql',
    '002_StoredProcedures.sql',
    '004_CreateAdminUser.sql',
    '005_AddConstraints.sql'
)

foreach ($f in $files) {
    $path = Join-Path $base $f
    Write-Host "Running $f..." -ForegroundColor Green
    Invoke-Sqlcmd -ServerInstance $Server -Database $Database -InputFile $path
}

Write-Host "All scripts executed." -ForegroundColor Green