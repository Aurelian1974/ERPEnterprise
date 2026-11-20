#Requires -Version 7.4
<#
.SYNOPSIS
    Porneste/opreste manual Erp.Host (BE) si Vite (FE) pentru dezvoltare locala.

.DESCRIPTION
    PID-urile si jurnalele sunt in artifacts/dev-run (ignorat de Git). Oprirea inchide procesul inregistrat si copiii lui, dupa PID.
    BE: https://localhost:7180 (profilul Erp.Host, Development). FE: http://localhost:5173 (proxy catre BE).

.EXAMPLE
    ./build/dev-services.ps1 -Action Start -Target Backend
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Start', 'Stop')][string]$Action,
    [Parameter(Mandatory)][ValidateSet('Backend', 'Frontend')][string]$Target
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$runDir = Join-Path $root 'artifacts/dev-run'
$pidFile = Join-Path $runDir "$Target.pid"
New-Item $runDir -ItemType Directory -Force | Out-Null

function Stop-Tree([int]$ProcessId) {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" | ForEach-Object { Stop-Tree $_.ProcessId }
    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

function Get-Running {
    if (-not (Test-Path $pidFile)) { return $null }
    $recorded = [int](Get-Content $pidFile -Raw)
    Get-Process -Id $recorded -ErrorAction SilentlyContinue
}

$running = Get-Running
if ($Action -eq 'Stop') {
    if ($running) { Stop-Tree $running.Id; Write-Host "$Target oprit (PID $($running.Id))." }
    else { Write-Host "$Target nu ruleaza." }
    Remove-Item $pidFile -ErrorAction SilentlyContinue
    return
}

if ($running) { Write-Host "$Target ruleaza deja (PID $($running.Id))."; return }

$port = if ($Target -eq 'Backend') { 7180 } else { 5173 }
$owner = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($owner) {
    $name = (Get-Process -Id $owner.OwningProcess -ErrorAction SilentlyContinue)?.ProcessName
    throw "Portul $port e ocupat de $name (PID $($owner.OwningProcess)); opreste-l sau foloseste dev-stop-*.ps1."
}

if ($Target -eq 'Backend') {
    $file = 'dotnet'
    $arguments = 'run --project src/Host/Erp.Host/Erp.Host.csproj --launch-profile Erp.Host'
    $workDir = $root
    $url = 'https://localhost:7180/health'
}
else {
    $file = 'cmd.exe'
    $arguments = '/c npm run dev'
    $workDir = Join-Path $root 'src/Web/erp-web'
    $url = 'http://localhost:5173'
}

$process = Start-Process $file -ArgumentList $arguments -WorkingDirectory $workDir -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $runDir "$Target.log") -RedirectStandardError (Join-Path $runDir "$Target.err.log")
$process.Id | Set-Content $pidFile
Write-Host "$Target pornit (PID $($process.Id)); asteptare $url ..."

for ($i = 0; $i -lt 90; $i++) {
    Start-Sleep 1
    if ($process.HasExited) { throw "$Target s-a oprit; vezi artifacts/dev-run/$Target.err.log si $Target.log." }
    try {
        if ((Invoke-WebRequest $url -SkipCertificateCheck -TimeoutSec 2 -ErrorAction Stop).StatusCode -eq 200) { Write-Host "$Target gata: $url"; return }
    }
    catch { }
}
throw "$Target nu raspunde la $url in 90 s; vezi jurnalele din artifacts/dev-run."
