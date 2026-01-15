# Run migration 039: Add IsStockable column to Articole table
# This script adds the IsStockable bit column to the Articole table

param(
    [string]$ServerInstance = "TS1828\ERP",
    [string]$Database = "ValyanERP"
)

Write-Host "=== Running Migration 039: Add IsStockable to Articole ===" -ForegroundColor Cyan
Write-Host "Server: $ServerInstance" -ForegroundColor Yellow
Write-Host "Database: $Database" -ForegroundColor Yellow
Write-Host ""

try {
    # Check if column already exists
    $checkQuery = @"
SELECT COUNT(*) as ColumnExists
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Articole'
AND COLUMN_NAME = 'IsStockable'
AND TABLE_SCHEMA = 'dbo'
"@

    $result = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $checkQuery

    if ($result.ColumnExists -gt 0) {
        Write-Host "⚠️  Column IsStockable already exists in Articole table. Skipping migration." -ForegroundColor Yellow
        exit 0
    }

    # Run the migration
    $scriptPath = Join-Path $PSScriptRoot "..\Scripts\039_Add_IsStockable_To_Articole.sql"

    if (!(Test-Path $scriptPath)) {
        throw "Migration script not found: $scriptPath"
    }

    Write-Host "📄 Executing migration script..." -ForegroundColor Green
    Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -InputFile $scriptPath

    Write-Host "✅ Migration 039 completed successfully!" -ForegroundColor Green

    # Verify the column was added
    $verifyQuery = @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Articole'
AND COLUMN_NAME = 'IsStockable'
AND TABLE_SCHEMA = 'dbo'
"@

    $verifyResult = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $verifyQuery

    if ($verifyResult) {
        Write-Host "🔍 Verification: Column IsStockable added successfully" -ForegroundColor Green
        Write-Host "   - Data Type: $($verifyResult.DATA_TYPE)" -ForegroundColor Gray
        Write-Host "   - Nullable: $($verifyResult.IS_NULLABLE)" -ForegroundColor Gray
        Write-Host "   - Default: $($verifyResult.COLUMN_DEFAULT)" -ForegroundColor Gray
    } else {
        throw "Column verification failed"
    }

} catch {
    Write-Host "❌ Migration 039 failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Migration 039 Complete ===" -ForegroundColor Cyan