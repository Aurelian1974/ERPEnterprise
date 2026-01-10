param(
    [string]$Server = "(localdb)\\MSSQLLocalDB",
    [string]$Database = "ValyanERP"
)

Write-Host "Using SQL Server instance: $Server" -ForegroundColor Cyan

# Check for sqlcmd
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Write-Host "sqlcmd not found. Install SQL Server Tools or run scripts from SSMS." -ForegroundColor Yellow
    exit 1
}

# Create database if it doesn't exist
$sqlCreateDb = "IF DB_ID(N'$Database') IS NULL CREATE DATABASE [$Database];"
sqlcmd -S $Server -Q $sqlCreateDb

# Run scripts in correct order
$base = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "Scripts"
$files = @(
    "003_Persoane.sql",    # Persoane first (FKs from Users)
    "001_CreateDatabase.sql", # Identity tables (Users, Roles, etc.)
    "002_StoredProcedures.sql", # Identity stored procedures
    "004_CreateAdminUser.sql"   # Optional seed admin (may require adjustment)
)

foreach ($f in $files) {
    $path = Join-Path $base $f
    if (-Not (Test-Path $path)) {
        Write-Host "Missing script: $path" -ForegroundColor Red
        exit 1
    }
    Write-Host "Running $f..." -ForegroundColor Green
    sqlcmd -S $Server -d $Database -i $path
}

Write-Host "Database $Database initialized successfully on $Server." -ForegroundColor Green
Write-Host "Note: If the admin seed script fails due to column mismatch, review Database/Scripts/004_CreateAdminUser.sql." -ForegroundColor Yellow