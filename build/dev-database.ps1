#Requires -Version 7.4
<#
.SYNOPSIS
    Creeaza sau actualizeaza baza de dezvoltare cu scripturile din depozit, fara installer.

.DESCRIPTION
    Ruleaza src/Tools/Erp.DevDatabase cu contul Windows curent si aceeasi sincronizare ca Erp.Host in Development
    (07, „Mediul de dezvoltare”): creeaza baza case sensitive daca lipseste (cu administratorul si Grupul Exemplu), aplica
    doar scripturile schimbate si o reface cand o migrare deja aplicata s-a modificat; optional administratorul initial,
    utilizatorii E2E si reincarcarea datelor demonstrative. Se poate rula de oricate ori.
    De obicei nu e nevoie de el: Erp.Host (Debug, Development) tine baza la zi singur, la pornire si la fiecare salvare.
    Numai pentru dezvoltare; o instalare reala se face cu ErpSetup.exe (Docs/arhitectura/08-instalare-actualizare).

.EXAMPLE
    ./build/dev-database.ps1 -Server Valeria -Database ERP_DEV -CreateAdmin

.EXAMPLE
    ./build/dev-database.ps1 -SampleData     # sterge firmele si incarca Grupul Exemplu (macheta 03)
#>
[CmdletBinding()]
param(
    [string]$Server = 'Valeria',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{2,63}$')][string]$Database = 'ERP_DEV',
    [ValidatePattern('^[A-Z][A-Z0-9]{1,9}$')][string]$Instance = 'DEV',
    [switch]$CreateAdmin,
    [ValidatePattern('^e2e\.[a-z0-9.]+$')][string]$E2EUser,
    [string]$PasswordFile,
    [ValidateSet('STANDARD', 'ADMIN')][string]$E2ERole = 'STANDARD',
    # Sterge structura organizatorica si incarca datele demonstrative din build/dev-sample-data.sql (macheta 03).
    [switch]$SampleData
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Scripturile se citesc direct din depozit, cu aceeasi sincronizare ca Erp.Host in Development (Erp.DevDatabase.Sync).
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$arguments = @('--server', $Server, '--database', $Database, '--instance', $Instance, '--repo', $repoRoot)
if ($CreateAdmin) { $arguments += '--create-admin' }
if ($SampleData) { $arguments += @('--sample-data', (Join-Path $PSScriptRoot 'dev-sample-data.sql')) }
if ($E2EUser) {
    if (-not $PasswordFile) { throw 'Pentru -E2EUser este obligatoriu -PasswordFile.' }
    $arguments += @('--e2e-user', $E2EUser, '--password-file', $PasswordFile, '--e2e-role', $E2ERole)
}
& dotnet run --project (Join-Path $repoRoot 'src/Tools/Erp.DevDatabase/Erp.DevDatabase.csproj') -- @arguments
if ($LASTEXITCODE -ne 0) { throw "Pregătirea bazei de dezvoltare a eșuat (cod $LASTEXITCODE)." }
