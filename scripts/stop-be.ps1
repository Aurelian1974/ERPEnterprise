# stop-be.ps1 — Opreste backend-ul ERP
# Ucide procesul care asculta pe portul 5042

$Port = 5042
Write-Host "[BE] Oprire proces pe portul $Port..." -ForegroundColor Yellow

$conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($null -eq $conn) {
    Write-Host "[BE] Niciun proces activ pe portul $Port." -ForegroundColor Gray
    exit 0
}

$pid = $conn.OwningProcess
$proc = Get-Process -Id $pid -ErrorAction SilentlyContinue
if ($null -ne $proc) {
    Write-Host "[BE] Oprire $($proc.Name) (PID $pid)..." -ForegroundColor Yellow
    Stop-Process -Id $pid -Force
    Write-Host "[BE] Oprit." -ForegroundColor Green
} else {
    Write-Host "[BE] Procesul cu PID $pid nu mai exista." -ForegroundColor Gray
}
