#Requires -Version 7.4
<#
.SYNOPSIS
    Construieste kitul de instalare ERP-<Versiune>-kit.zip (INSTALARE_ACTUALIZARE.md, sectiunile 4 si 8.5).

.DESCRIPTION
    1. Verifica scripturile SQL (nume MIG-01, prefixe unice, CREATE OR ALTER, fara dbo., UTF-8 fara BOM, MIG-02).
    2. Publica Erp.Host, Erp.Worker (framework-dependent, win-x64) si ErpSetup.exe (self-contained, single-file),
       cu cheia publica a kitului incorporata.
    3. Copiaza scripturile, sabloanele si Hosting Bundle-ul; semneaza Authenticode binarele proprii (release).
    4. Genereaza manifest.json (SHA-256 pentru fiecare fisier) si manifest.sig (ECDSA P-256 / SHA-256), apoi arhiveaza.

    -TestKit: kit de test, build Debug, pereche de chei generata pe loc (numai pentru CI si teste), fara Authenticode.
    Cheia privata de productie nu sta in depozit (build/kit-signing/README.md).

.EXAMPLE
    ./build/package-kit.ps1 -Version 2026.0.1 -TestKit

.EXAMPLE
    ./build/package-kit.ps1 -Version 2027.1.0 -SigningKeyPath K:\kit-signing\kit-private.pem -PublicKeyPath K:\kit-signing\kit-public.pem `
        -CodeSigningCertThumbprint 3F2A... -HostingBundlePath D:\Prereq\dotnet-hosting-10.0.3-win.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d{4}\.\d+\.\d+$')][string]$Version,
    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')][string]$ReleaseDate = [DateTime]::UtcNow.ToString('yyyy-MM-dd'),
    [ValidatePattern('^\d{4}\.\d+\.\d+$')][string]$MinUpgradeFrom,
    [string]$PreviousTag,
    [string]$HostingBundlePath,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$HostingBundleVersion,
    [string]$SigningKeyPath,
    [string]$PublicKeyPath,
    [string]$CodeSigningCertThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$OutputDir = (Join-Path $PSScriptRoot '..' 'artifacts'),
    [switch]$TestKit
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'KitBuilder.psm1') -Force

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$configuration = if ($TestKit) { 'Debug' } else { 'Release' }
$kitName = "ERP-$Version-kit"
$kitRoot = Join-Path $OutputDir $kitName
$work = Join-Path $OutputDir "work-$Version"

function Invoke-Native {
    param([string]$Description, [scriptblock]$Command)
    Write-Host "  $Description"
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Description a eșuat (cod $LASTEXITCODE)." }
}

# --- Parametri obligatorii pentru un kit livrabil ---
if (-not $TestKit) {
    $missing = @()
    if (-not $SigningKeyPath) { $missing += '-SigningKeyPath' }
    if (-not $PublicKeyPath) { $missing += '-PublicKeyPath' }
    if (-not $CodeSigningCertThumbprint) { $missing += '-CodeSigningCertThumbprint' }
    if (-not $HostingBundlePath) { $missing += '-HostingBundlePath' }
    if ($missing) { throw "Un kit livrabil cere: $($missing -join ', '). Pentru un kit de test folosiți -TestKit." }
}

if ($HostingBundlePath) {
    $bundleName = Split-Path $HostingBundlePath -Leaf
    if ($bundleName -notmatch '^dotnet-hosting-(\d+\.\d+\.\d+)-win\.exe$') { throw "Numele Hosting Bundle nu are forma dotnet-hosting-<versiune>-win.exe: $bundleName" }
    if ($HostingBundleVersion -and $HostingBundleVersion -ne $Matches[1]) { throw 'HostingBundleVersion nu corespunde fișierului dat.' }
    $HostingBundleVersion = $Matches[1]
}
elseif (-not $HostingBundleVersion) {
    $HostingBundleVersion = '10.0.0'
}

# --- 1. Verificarile de build pentru scripturi (8.5) ---
Write-Host '1. Verific scripturile SQL'
$tags = @(Get-KitVersionTags -RepoRoot $repoRoot)
if (-not $PreviousTag) { $PreviousTag = Get-KitPreviousTag -Version $Version -Tags $tags }
if (-not $PreviousTag) { Write-Host '  Nu există o versiune livrată anterior; verificarea MIG-02 nu se aplică.' }
$scriptErrors = Test-KitScripts -RepoRoot $repoRoot -PreviousTag $PreviousTag
if ($scriptErrors.Count -gt 0) {
    $scriptErrors | ForEach-Object { Write-Host "  EROARE: $_" -ForegroundColor Red }
    throw "Verificarea scripturilor a eșuat ($($scriptErrors.Count) erori)."
}

$migrations = Get-KitMigrations -RepoRoot $repoRoot
$lastMigration = if ($migrations.Count -gt 0) { $migrations[-1].Name } else { $null }
if (-not $lastMigration -and -not $TestKit) { throw 'Un kit livrabil conține cel puțin o migrare (lastMigration).' }
if (-not $MinUpgradeFrom) {
    $MinUpgradeFrom = Get-KitMinUpgradeFrom -Version $Version -ReleaseDate ([datetime]::ParseExact($ReleaseDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)) -Tags $tags
}

# --- Cheile kitului ---
if (Test-Path $kitRoot) { Remove-Item $kitRoot -Recurse -Force }
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $kitRoot, $work | Out-Null

if ($TestKit) {
    $testKeys = New-Item -ItemType Directory -Force (Join-Path $OutputDir 'kit-signing-test')
    $generated = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
    $SigningKeyPath = Join-Path $testKeys 'kit-test-private.pem'
    $PublicKeyPath = Join-Path $testKeys 'kit-test-public.pem'
    [IO.File]::WriteAllText($SigningKeyPath, $generated.ExportPkcs8PrivateKeyPem())
    [IO.File]::WriteAllText($PublicKeyPath, $generated.ExportSubjectPublicKeyInfoPem())
    $generated.Dispose()
}

$signingKey = Import-KitSigningKey -Path $SigningKeyPath
$publicKey = [Security.Cryptography.ECDsa]::Create()
$publicKey.ImportFromPem((Get-Content -Raw -LiteralPath $PublicKeyPath))
Assert-P256 $publicKey
if ($signingKey.ExportSubjectPublicKeyInfoPem() -ne $publicKey.ExportSubjectPublicKeyInfoPem()) {
    throw 'Cheia publică nu corespunde cheii private de semnare a kitului.'
}

try {
    # --- 2. Publicare ---
    Write-Host "2. Public aplicațiile ($configuration, versiunea $Version)"
    $common = @('-c', $configuration, "-p:Version=$Version", '-p:DebugType=None', '-p:DebugSymbols=false', '--nologo')
    # 10.5: ultima migrare se incorporeaza in Erp.Host si Erp.Worker (AssemblyMetadata ErpLastMigration).
    if ($lastMigration) { $common += "-p:ErpLastMigration=$lastMigration" }
    Invoke-Native 'Erp.Host' { dotnet publish (Join-Path $repoRoot 'src/Host/Erp.Host/Erp.Host.csproj') @common -o (Join-Path $kitRoot 'app/host') }
    Invoke-Native 'Erp.Worker' { dotnet publish (Join-Path $repoRoot 'src/Worker/Erp.Worker/Erp.Worker.csproj') @common -o (Join-Path $kitRoot 'app/worker') }
    $setupOut = Join-Path $work 'setup'
    Invoke-Native 'ErpSetup' { dotnet publish (Join-Path $repoRoot 'src/Setup/Erp.Setup/Erp.Setup.csproj') @common -o $setupOut "-p:KitPublicKeyFile=$PublicKeyPath" }
    Copy-Item (Join-Path $setupOut 'ErpSetup.exe') $kitRoot

    # --- 3. Scripturi, sabloane, cerinte preliminare ---
    Write-Host '3. Copiez scripturile, șabloanele și cerințele preliminare'
    Copy-KitDatabaseScripts -RepoRoot $repoRoot -KitRoot $kitRoot
    $kitFiles = Join-Path $repoRoot 'src/Setup/Erp.Setup/Kit'
    Copy-Item (Join-Path $kitFiles '*') $kitRoot -Recurse -Force
    if ($HostingBundlePath) {
        $prerequisites = New-Item -ItemType Directory -Force (Join-Path $kitRoot 'prerequisites')
        Copy-Item -LiteralPath $HostingBundlePath $prerequisites
    }

    if (-not $TestKit) {
        $required = 'CITESTE-MA.txt', 'THIRD-PARTY-NOTICES.txt', 'templates/appsettings.Installation.json', 'templates/maintenance.html', 'templates/answers.example.json'
        $absent = @($required | Where-Object { -not (Test-Path (Join-Path $kitRoot $_)) })
        if ($absent) { throw "Kitul livrabil nu are: $($absent -join ', ') (sursa: src/Setup/Erp.Setup/Kit)." }

        Write-Host '   Semnez Authenticode binarele proprii'
        $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue)?.Source
        if (-not $signtool) {
            $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
                Sort-Object FullName | Select-Object -Last 1 -ExpandProperty FullName
        }
        if (-not $signtool) { throw 'signtool.exe nu a fost găsit (Windows SDK).' }
        $ownBinaries = @(Get-Item (Join-Path $kitRoot 'ErpSetup.exe')) + @(Get-ChildItem (Join-Path $kitRoot 'app') -Recurse -File -Include 'Erp.*.dll', 'Erp.*.exe')
        Invoke-Native 'signtool' { & $signtool sign /sha1 $CodeSigningCertThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 @($ownBinaries.FullName) }
    }

    # --- 4. Manifest, semnatura, arhiva ---
    Write-Host '4. Generez și semnez manifestul'
    New-KitManifest -KitRoot $kitRoot -SigningKey $signingKey -Metadata @{
        Version              = $Version
        ReleaseDate          = $ReleaseDate
        MinUpgradeFrom       = $MinUpgradeFrom
        HostingBundleVersion = $HostingBundleVersion
        LastMigration        = $lastMigration
    }

    $zip = Join-Path $OutputDir "$kitName.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $kitRoot -DestinationPath $zip
    Write-Host "Kit generat: $zip" -ForegroundColor Green
    if ($TestKit) { Write-Host "KIT DE TEST (cheie generată: $PublicKeyPath). Nu se livrează." -ForegroundColor Yellow }
}
finally {
    $signingKey.Dispose()
    $publicKey.Dispose()
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
}
