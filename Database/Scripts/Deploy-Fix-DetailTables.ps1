# Deploy-Fix-DetailTables.ps1
# Applies the detail table saving fix to the database

param(
    [string]$ServerInstance = "TS1828\ERP",
    [string]$Database = "ValyanERP"
)

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Deploying Detail Table Saving Fix" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Server: $ServerInstance" -ForegroundColor Yellow
Write-Host "Database: $Database" -ForegroundColor Yellow
Write-Host ""

# Get the script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Define the scripts to run in order
$scripts = @(
    "054_StoredProcedures_Invoice_Update.sql",
    "056_StoredProcedures_Invoice_ExtendParams.sql"
)

$totalScripts = $scripts.Count
$currentScript = 0

foreach ($script in $scripts) {
    $currentScript++
    $scriptPath = Join-Path $scriptDir $script

    Write-Host "[$currentScript/$totalScripts] Executing: $script" -ForegroundColor Green

    if (-not (Test-Path $scriptPath)) {
        Write-Host "  ERROR: Script not found: $scriptPath" -ForegroundColor Red
        exit 1
    }

    try {
        # Use Invoke-Sqlcmd if available
        if (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue) {
            Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -InputFile $scriptPath -ErrorAction Stop
            Write-Host "  SUCCESS: Script executed successfully" -ForegroundColor Green
        }
        # Fallback to sqlcmd.exe
        else {
            $result = & sqlcmd -S $ServerInstance -d $Database -i $scriptPath -b
            if ($LASTEXITCODE -ne 0) {
                throw "sqlcmd failed with exit code $LASTEXITCODE"
            }
            Write-Host "  SUCCESS: Script executed successfully" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "  ERROR: Failed to execute script" -ForegroundColor Red
        Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }

    Write-Host ""
}

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Deployment completed successfully!" -ForegroundColor Green
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "The following fixes have been applied:" -ForegroundColor Yellow
Write-Host "  - sp_InvoiceDetail_UpdateLineItems now properly handles DocumentDetail and InvoiceDetail" -ForegroundColor White
Write-Host "  - sp_Invoice_UpdateComplete now passes DocumentId and owner fields" -ForegroundColor White
Write-Host ""
Write-Host "You can now create and update invoices with detail line items." -ForegroundColor Green
