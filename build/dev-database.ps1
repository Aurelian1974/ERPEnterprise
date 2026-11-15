#Requires -Version 7.4
<#
.SYNOPSIS
    Creeaza sau actualizeaza baza de dezvoltare cu scripturile din depozit, fara installer.

.DESCRIPTION
    Aseaza scripturile SQL ca in kit (aceeasi functie ca package-kit.ps1), apoi ruleaza src/Tools/Erp.DevDatabase cu contul
    Windows curent: creeaza baza case sensitive, bootstrap, migratii DbUp, obiecte programabile, date livrate si, optional,
    administratorul initial. Se poate rula de oricate ori: aplica doar migratiile noi.
    Numai pentru dezvoltare; o instalare reala se face cu ErpSetup.exe (Docs/arhitectura/08-instalare-actualizare).

.EXAMPLE
    ./build/dev-database.ps1 -Server Valeria -Database ERP_DEV -CreateAdmin
#>
[CmdletBinding()]
param(
    [string]$Server = 'Valeria',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{2,63}$')][string]$Database = 'ERP_DEV',
    [ValidatePattern('^[A-Z][A-Z0-9]{1,9}$')][string]$Instance = 'DEV',
    [switch]$CreateAdmin,
    [ValidatePattern('^e2e\.[a-z0-9.]+$')][string]$E2EUser,
    [string]$PasswordFile,
    [ValidateSet('STANDARD', 'ADMIN')][string]$E2ERole = 'STANDARD'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'KitBuilder.psm1') -Force

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$staging = Join-Path ([IO.Path]::GetTempPath()) ("erp-devdb-" + [guid]::NewGuid().ToString('N'))
try {
    Copy-KitDatabaseScripts -RepoRoot $repoRoot -KitRoot $staging
    $arguments = @('--server', $Server, '--database', $Database, '--instance', $Instance, '--scripts', (Join-Path $staging 'database'))
    if ($CreateAdmin) { $arguments += '--create-admin' }
    if ($E2EUser) {
        if (-not $PasswordFile) { throw 'Pentru -E2EUser este obligatoriu -PasswordFile.' }
        $arguments += @('--e2e-user', $E2EUser, '--password-file', $PasswordFile, '--e2e-role', $E2ERole)
    }
    & dotnet run --project (Join-Path $repoRoot 'src/Tools/Erp.DevDatabase/Erp.DevDatabase.csproj') -- @arguments
    if ($LASTEXITCODE -ne 0) { throw "Pregătirea bazei de dezvoltare a eșuat (cod $LASTEXITCODE)." }
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}
