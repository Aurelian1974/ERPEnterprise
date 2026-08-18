# stop-fe.ps1 — Opreste frontend-ul ERP (Vite dev server)
# Ucide procesul care asculta pe portul 5173

$Port = 5173
Write-Host "[FE] Oprire proces pe portul $Port..." -ForegroundColor Yellow

$conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($null -eq $conn) {
    Write-Host "[FE] Niciun proces activ pe portul $Port." -ForegroundColor Gray
    exit 0
}

$pid = $conn.OwningProcess
$proc = Get-Process -Id $pid -ErrorAction SilentlyContinue
if ($null -ne $proc) {
    Write-Host "[FE] Oprire $($proc.Name) (PID $pid)..." -ForegroundColor Yellow
    Stop-Process -Id $pid -Force
    Write-Host "[FE] Oprit." -ForegroundColor Green
} else {
    Write-Host "[FE] Procesul cu PID $pid nu mai exista." -ForegroundColor Gray
}
