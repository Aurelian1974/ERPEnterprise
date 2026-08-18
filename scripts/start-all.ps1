# start-all.ps1 — Porneste ERP complet (BE + FE) in ferestre separate
# Rulare: .\scripts\start-all.ps1

$Scripts = $PSScriptRoot

Write-Host "=== ERP Start All ===" -ForegroundColor Cyan

# Porneste BE in fereastra noua
Write-Host "[ALL] Pornire backend in fereastra noua..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-File", "`"$Scripts\start-be.ps1`""

# Asteapta 2 secunde inainte de FE (optional, BE porneste in paralel oricum)
Start-Sleep -Seconds 2

# Porneste FE in fereastra noua
Write-Host "[ALL] Pornire frontend in fereastra noua..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-File", "`"$Scripts\start-fe.ps1`""

Write-Host "" -ForegroundColor White
Write-Host "Backend:  http://localhost:5042" -ForegroundColor Green
Write-Host "Frontend: http://localhost:5173" -ForegroundColor Green
Write-Host "Swagger:  http://localhost:5042/swagger" -ForegroundColor Green
