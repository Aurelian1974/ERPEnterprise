<#
.SYNOPSIS
    End of day (GIT-06): commit, push, kit nou si tag pe commit.
.DESCRIPTION
    1. opreste serviciile dev (blocheaza build-ul) si ruleaza testele unitare .NET;
    2. commit (daca exista modificari) si push pe branch-ul curent;
    3. calculeaza versiunea urmatoare AAAA.LL.Major.Release.ServicePack din ultimul tag;
    4. construieste kitul (de test daca lipsesc cheile de semnare) din commitul trimis;
    5. pune tagul pe commit si il publica; reporneste serviciile dev.
    Major si Release se schimba manual cu -Major / -Release; ServicePack creste automat.
.EXAMPLE
    ./build/eod.ps1 -Message "Corectie licente"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Message,
    [int]$Major = -1,
    [int]$Release = -1,
    [string]$SigningKeyPath,
    [string]$PublicKeyPath,
    [string]$CodeSigningCertThumbprint,
    [string]$HostingBundlePath,
    [switch]$SkipTests,
    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root
Import-Module (Join-Path $PSScriptRoot 'KitBuilder.psm1') -Force

function Invoke-Git { & git @args; if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') a esuat." } }

$branch = (& git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -eq 'HEAD') { throw 'HEAD detasat; treceti pe un branch.' }

& (Join-Path $PSScriptRoot 'dev-stop-backend.ps1')
& (Join-Path $PSScriptRoot 'dev-stop-frontend.ps1')

if (-not $SkipTests) {
    & dotnet test --solution Erp.slnx --filter-not-trait 'Category=Integration'
    if ($LASTEXITCODE -ne 0) { throw 'Testele au esuat; nu se face commit.' }
}

if (& git status --porcelain) {
    Invoke-Git add -A
    Invoke-Git commit -m $Message -m 'Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>'
}
Invoke-Git push origin $branch
$commit = (& git rev-parse HEAD).Trim()

# Versiunea urmatoare: AAAA.LL din data, Major.Release din ultimul tag (sau argumente), ServicePack + 1
Invoke-Git fetch --tags
$last = Get-KitVersionTags -RepoRoot $root | Sort-Object { Get-VersionSortKey $_.Version } | Select-Object -Last 1
$p = if ($last) { @($last.Version.Split('.') | ForEach-Object { [int]$_ }) + @(0, 0, 0, 0, 0) } else { @(0, 0, 1, 0, 0) }
$now = Get-Date
$mj = if ($Major -ge 0) { $Major } else { $p[2] }
$rl = if ($Release -ge 0) { $Release } else { $p[3] }
$sp = if ($p[0] -eq $now.Year -and $p[1] -eq $now.Month -and $p[2] -eq $mj -and $p[3] -eq $rl) { $p[4] + 1 } else { 1 }
$version = '{0}.{1}.{2}.{3}.{4}' -f $now.Year, $now.Month, $mj, $rl, $sp

$kitArgs = @{ Version = $version }
if ($SigningKeyPath -and $PublicKeyPath -and $CodeSigningCertThumbprint) {
    $kitArgs += @{ SigningKeyPath = $SigningKeyPath; PublicKeyPath = $PublicKeyPath; CodeSigningCertThumbprint = $CodeSigningCertThumbprint }
}
else {
    Write-Warning 'Fara chei de semnare: kit de test (nu se livreaza).'
    $kitArgs.TestKit = $true
}
$bundle = if ($HostingBundlePath) { $HostingBundlePath } else { Get-ChildItem (Join-Path $root 'artifacts\prereq') -Filter 'dotnet-hosting-*-win.exe' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName }
if ($bundle) { $kitArgs.HostingBundlePath = $bundle }

& (Join-Path $PSScriptRoot 'package-kit.ps1') @kitArgs

Invoke-Git tag $version $commit
Invoke-Git push origin $version
Write-Host "Gata: commit $($commit.Substring(0, 7)), tag $version, kit in artifacts."

if (-not $NoRestart) {
    foreach ($s in 'dev-start-backend.ps1', 'dev-start-frontend.ps1') {
        Start-Process pwsh -WindowStyle Hidden -ArgumentList '-NoProfile', '-File', (Join-Path $PSScriptRoot $s)
    }
}
