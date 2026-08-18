# stop-all.ps1 — Opreste ERP complet (BE + FE)
# Rulare: .\scripts\stop-all.ps1

$Scripts = $PSScriptRoot

Write-Host "=== ERP Stop All ===" -ForegroundColor Yellow

& "$Scripts\stop-be.ps1"
& "$Scripts\stop-fe.ps1"

Write-Host "=== Toate procesele ERP au fost oprite ===" -ForegroundColor Green
