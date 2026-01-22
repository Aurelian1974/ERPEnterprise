@echo off
REM Deploy-Fix-DetailTables.cmd
REM Applies the detail table saving fix to the database

set SERVER=TS1828\ERP
set DATABASE=ValyanERP

echo ================================================
echo Deploying Detail Table Saving Fix
echo ================================================
echo.
echo Server: %SERVER%
echo Database: %DATABASE%
echo.

cd /d %~dp0

echo [1/2] Executing: 054_StoredProcedures_Invoice_Update.sql
sqlcmd -S %SERVER% -d %DATABASE% -E -i 054_StoredProcedures_Invoice_Update.sql
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to execute script 054
    pause
    exit /b 1
)
echo SUCCESS: Script 054 executed
echo.

echo [2/2] Executing: 056_StoredProcedures_Invoice_ExtendParams.sql
sqlcmd -S %SERVER% -d %DATABASE% -E -i 056_StoredProcedures_Invoice_ExtendParams.sql
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to execute script 056
    pause
    exit /b 1
)
echo SUCCESS: Script 056 executed
echo.

echo ================================================
echo Deployment completed successfully!
echo ================================================
echo.
echo The following fixes have been applied:
echo   - sp_InvoiceDetail_UpdateLineItems now properly handles DocumentDetail and InvoiceDetail
echo   - sp_Invoice_UpdateComplete now passes DocumentId and owner fields
echo.
echo You can now create and update invoices with detail line items.
echo.
pause
