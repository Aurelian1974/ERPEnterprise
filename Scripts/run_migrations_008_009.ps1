# Run migrations 008 and 009 - Security fixes stored procedures
# Fixes SQL Injection vulnerabilities

$ErrorActionPreference = "Stop"

Write-Host "Running Security Fixes - Stored Procedures Migrations" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

$server = "TS1828\ERP"
$database = "ValyanERP"

# Migration 008 - Persoane Stored Procedures
Write-Host "Running 008_StoredProcedures_Persoane.sql..." -ForegroundColor Yellow
$migration008 = Get-Content -Path ".\Database\Scripts\008_StoredProcedures_Persoane.sql" -Raw
try {
    Invoke-Sqlcmd -ServerInstance $server -Database $database -Query $migration008 -Verbose
    Write-Host "Migration 008 completed successfully!" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "Migration 008 failed: $_" -ForegroundColor Red
    exit 1
}

# Migration 009 - Users Stored Procedures
Write-Host "Running 009_StoredProcedures_Users.sql..." -ForegroundColor Yellow
$migration009 = Get-Content -Path ".\Database\Scripts\009_StoredProcedures_Users.sql" -Raw
try {
    Invoke-Sqlcmd -ServerInstance $server -Database $database -Query $migration009 -Verbose
    Write-Host "Migration 009 completed successfully!" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "Migration 009 failed: $_" -ForegroundColor Red
    exit 1
}

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "All security migrations completed!" -ForegroundColor Green
Write-Host ""
Write-Host "Summary of changes:" -ForegroundColor Cyan
Write-Host "  - sp_Persoane_GetPaged (SQL injection safe)" -ForegroundColor White
Write-Host "  - sp_Persoane_GetById" -ForegroundColor White
Write-Host "  - sp_Persoane_GetByEmail" -ForegroundColor White
Write-Host "  - sp_Persoane_GetAllSimple (limited to 1000 records)" -ForegroundColor White
Write-Host "  - sp_Persoane_Create" -ForegroundColor White
Write-Host "  - sp_Persoane_Update" -ForegroundColor White
Write-Host "  - sp_Persoane_Delete (soft delete)" -ForegroundColor White
Write-Host "  - sp_Users_GetPaged (SQL injection safe)" -ForegroundColor White
Write-Host ""
