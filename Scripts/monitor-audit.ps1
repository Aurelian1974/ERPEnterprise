Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   AUDIT SYSTEM TEST MONITOR" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Instructions:" -ForegroundColor Yellow
Write-Host "1. Open: http://localhost:5082" -ForegroundColor White
Write-Host "2. Login: admin@valyanerp.ro" -ForegroundColor White
Write-Host "3. Go to: /administrare/parametri-sistem" -ForegroundColor White
Write-Host "4. Edit: Business.Pagination.DefaultPageSize" -ForegroundColor White
Write-Host "5. Change value and Save" -ForegroundColor White
Write-Host ""
Write-Host "Monitoring... (Ctrl+C to stop)" -ForegroundColor Green
Write-Host ""

$i = 0
while ($true) {
    $i++
    $time = Get-Date -Format "HH:mm:ss"
    Write-Host "[$i] Checking at $time..." -ForegroundColor Cyan
    
    $sql = "SELECT TOP 3 OperationType, UserEmail, CONVERT(VARCHAR,Timestamp,120) as Time, ChangedFieldsCount, Notes FROM AuditLogs WHERE EntityType='SystemParameters' ORDER BY Timestamp DESC"
    
    $logs = Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -Query $sql -ErrorAction SilentlyContinue
    
    if ($logs) {
        Write-Host "  Found audit logs:" -ForegroundColor Green
        $logs | Format-Table -AutoSize | Out-String | Write-Host
    } else {
        Write-Host "  No logs yet..." -ForegroundColor Yellow
    }
    
    Start-Sleep -Seconds 5
}
