# Test Audit System - Monitor logs and database
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "   AUDIT SYSTEM TEST MONITOR" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Instructions:" -ForegroundColor Yellow
Write-Host "1. Open browser: http://localhost:5082" -ForegroundColor White
Write-Host "2. Login as admin@valyanerp.ro / Admin@123" -ForegroundColor White
Write-Host "3. Navigate to: /administrare/parametri-sistem" -ForegroundColor White
Write-Host "4. Edit parameter: Business.Pagination.DefaultPageSize" -ForegroundColor White
Write-Host "5. Change value and Save" -ForegroundColor White
Write-Host ""
Write-Host "Monitoring for changes (checks every 5 seconds)..." -ForegroundColor Green
Write-Host "Press Ctrl+C to stop" -ForegroundColor DarkGray
Write-Host ""

$checkCount = 0

while ($true) {
    $checkCount++
    $timestamp = Get-Date -Format "HH:mm:ss"
    Write-Host "[$checkCount] Checking at $timestamp..." -ForegroundColor Cyan
    
    try {
        $query = "SELECT TOP 3 EntityType, OperationType, UserEmail, CONVERT(VARCHAR, Timestamp, 120) as Timestamp, ChangedFieldsCount, Notes FROM AuditLogs WHERE EntityType = 'SystemParameters' ORDER BY Timestamp DESC"
        
        $results = Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -Query $query -ErrorAction Stop
        
        if ($results) {
            $count = @($results).Count
            Write-Host "  ✅ Found $count SystemParameters audit log(s)" -ForegroundColor Green
            
            foreach ($log in $results) {
                Write-Host "    📝 $($log.OperationType) by $($log.UserEmail)" -ForegroundColor Yellow
                Write-Host "       Time: $($log.Timestamp) | Changes: $($log.ChangedFieldsCount)" -ForegroundColor White
                Write-Host "       Notes: $($log.Notes)" -ForegroundColor Gray
            }
        } else {
            Write-Host "  ⏳ No logs yet - waiting for parameter change..." -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  ❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    Write-Host ""
    Start-Sleep -Seconds 5
}
