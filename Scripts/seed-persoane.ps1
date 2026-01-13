# Seed Persoane data
# Run this script to populate the Persoane table with test data

$ErrorActionPreference = "Stop"

Write-Host "Seeding Persoane table with test data..." -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan

$server = "TS1828\ERP"
$database = "ValyanERP"

# Run seed script
Write-Host "Running 032_Seed_Persoane.sql..." -ForegroundColor Yellow
$seedScript = Get-Content -Path ".\Database\Scripts\032_Seed_Persoane.sql" -Raw
try {
    Invoke-Sqlcmd -ServerInstance $server -Database $database -Query $seedScript -Verbose
    Write-Host "Persoane seed data inserted successfully!" -ForegroundColor Green
} catch {
    Write-Host "Failed to seed Persoane data: $_" -ForegroundColor Red
    exit 1
}

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Persoane table seeded with 10 test records!" -ForegroundColor Green