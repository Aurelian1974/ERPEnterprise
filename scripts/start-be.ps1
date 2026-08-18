# start-be.ps1 — Porneste backend-ul ERP (build + run)
# Port: http://localhost:5042
# Rulare: .\scripts\start-be.ps1

$Root = Split-Path $PSScriptRoot -Parent
$ApiDir = Join-Path $Root "src\Api"

Write-Host "[BE] Build solution..." -ForegroundColor Cyan
dotnet build "$Root\erp.slnx" --configuration Release --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "[BE] Build ESUAT. Verifica erorile de mai sus." -ForegroundColor Red
    exit 1
}

Write-Host "[BE] Pornire API pe http://localhost:5042 ..." -ForegroundColor Green
Set-Location $ApiDir
dotnet run --configuration Release --no-build --launch-profile http
