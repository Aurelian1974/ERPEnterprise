# start-fe.ps1 — Porneste frontend-ul ERP (Vite dev server)
# Port: http://localhost:5173
# Rulare: .\scripts\start-fe.ps1

$Root = Split-Path $PSScriptRoot -Parent
$FrontendDir = Join-Path $Root "frontend"

Write-Host "[FE] Pornire Vite dev server pe http://localhost:5173 ..." -ForegroundColor Green
Set-Location $FrontendDir
npm run dev
