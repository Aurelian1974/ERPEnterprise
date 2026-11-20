#Requires -Version 7.4
Set-StrictMode -Version Latest

# Functiile folosite de package-kit.ps1 (INSTALARE_ACTUALIZARE.md, 4 si 8.5), testate cu Pester in build/tests.

# Ordinea de dependenta a modulelor (8.5); aceeasi lista va fi in Erp.Setup.Sql/ModuleOrder.cs (T7).
$script:ModuleOrder = @('core', 'ids', 'hangfire', 'intg', 'fin', 'inv', 'sales', 'purch', 'wms', 'tms', 'mfg', 'hr', 'pos', 'cons', 'crm', 'rpt')

# src/Modules/<Modul> -> folderul din kit (= schema).
$script:ModuleSchemas = [ordered]@{
    Core = 'core'; Finance = 'fin'; Sales = 'sales'; Purchasing = 'purch'; Inventory = 'inv'; Integrations = 'intg'
    Wms = 'wms'; Tms = 'tms'; Manufacturing = 'mfg'; Hr = 'hr'; Pos = 'pos'; Construction = 'cons'; Crm = 'crm'; Reporting = 'rpt'
}

$script:MigrationNamePattern = '^\d{4}\.\d{2}\.\d{3}_[A-Za-z0-9_]+\.sql$'

function Get-KitModuleOrder { , $script:ModuleOrder }

function Get-KitDatabaseSources {
    <# Folderele Database/ din depozit, cu schema corespunzatoare; erori pentru module necunoscute. #>
    param([Parameter(Mandatory)][string]$RepoRoot)

    $sources = [System.Collections.Generic.List[object]]::new()
    $errors = [System.Collections.Generic.List[string]]::new()

    $modulesRoot = Join-Path $RepoRoot 'src/Modules'
    if (Test-Path $modulesRoot) {
        foreach ($module in Get-ChildItem $modulesRoot -Directory | Sort-Object Name) {
            $database = Join-Path $module.FullName "Erp.$($module.Name).Infrastructure/Database"
            if (-not $script:ModuleSchemas.Contains($module.Name)) {
                if (Test-Path $database) { $errors.Add("Modul necunoscut pentru kit: src/Modules/$($module.Name) (adăugați-l în maparea modul -> schemă).") }
                continue
            }
            if (Test-Path $database) { $sources.Add([pscustomobject]@{ Schema = $script:ModuleSchemas[$module.Name]; Path = $database }) }
        }
    }

    foreach ($special in @(@{ Schema = 'ids'; Path = 'src/Auth/Erp.Auth/Database' }, @{ Schema = 'hangfire'; Path = 'src/Worker/Erp.Worker/Database' })) {
        $database = Join-Path $RepoRoot $special.Path
        if (Test-Path $database) { $sources.Add([pscustomobject]@{ Schema = $special.Schema; Path = $database }) }
    }

    [pscustomobject]@{ Sources = $sources; Errors = $errors }
}

function Remove-SqlComments {
    param([string]$Sql)
    $withoutBlocks = [regex]::Replace($Sql, '/\*.*?\*/', ' ', 'Singleline')
    [regex]::Replace($withoutBlocks, '--[^\r\n]*', ' ')
}

function Read-StrictUtf8 {
    <# Intoarce textul sau $null daca fisierul are BOM ori nu e UTF-8 valid (8.5: UTF-8 fara BOM). #>
    param([Parameter(Mandatory)][string]$Path, [ref]$Problem)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $Problem.Value = 'are BOM (se cere UTF-8 fără BOM)'
        return $null
    }
    try {
        return [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    }
    catch [Text.DecoderFallbackException] {
        $Problem.Value = 'nu este UTF-8 valid'
        return $null
    }
}

function Test-KitScripts {
    <#
    Verificarile de build pentru scripturi (8.5): nume MIG-01, prefixe unice per modul, CREATE OR ALTER in Programmability,
    fara dbo. in afara msdb.dbo./master.dbo., UTF-8 fara BOM si, daca exista un tag anterior, MIG-02 (migratii nemodificate).
    Intoarce lista erorilor; goala = scripturi valide.
    #>
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [string]$PreviousTag
    )

    $found = Get-KitDatabaseSources -RepoRoot $RepoRoot
    $errors = [System.Collections.Generic.List[string]]::new()
    $found.Errors | ForEach-Object { $errors.Add($_) }

    $scripts = [System.Collections.Generic.List[object]]::new()
    $bootstrap = Join-Path $RepoRoot 'src/Setup/Erp.Setup.Sql/Database/Bootstrap'
    if (Test-Path $bootstrap) {
        Get-ChildItem $bootstrap -Filter *.sql -File | ForEach-Object { $scripts.Add([pscustomobject]@{ Kind = 'Bootstrap'; Schema = $null; File = $_ }) }
    }
    foreach ($source in $found.Sources) {
        foreach ($kind in 'Migrations', 'Programmability', 'Seed') {
            $folder = Join-Path $source.Path $kind
            if (-not (Test-Path $folder)) { continue }
            $recurse = $kind -eq 'Programmability'
            Get-ChildItem $folder -Filter *.sql -File -Recurse:$recurse | ForEach-Object {
                $scripts.Add([pscustomobject]@{ Kind = $kind; Schema = $source.Schema; File = $_ })
            }
        }
    }

    $prefixes = @{}
    foreach ($item in $scripts) {
        $relative = [IO.Path]::GetRelativePath($RepoRoot, $item.File.FullName).Replace('\', '/')
        $problem = $null
        $text = Read-StrictUtf8 -Path $item.File.FullName -Problem ([ref]$problem)
        if ($null -eq $text) { $errors.Add("${relative}: $problem."); continue }
        $code = Remove-SqlComments $text

        if ($item.Kind -eq 'Migrations') {
            if ($item.File.Name -cnotmatch $script:MigrationNamePattern) {
                $errors.Add("${relative}: numele nu respectă MIG-01 (AAAA.LL.NNN_Descriere.sql).")
            }
            else {
                $key = "$($item.Schema)|$($item.File.Name.Substring(0, 11))"
                if ($prefixes.ContainsKey($key)) {
                    $errors.Add("${relative}: prefixul $($item.File.Name.Substring(0, 11)) este folosit și de $($prefixes[$key]) în modulul $($item.Schema).")
                }
                else { $prefixes[$key] = $item.File.Name }
            }
        }

        if ($item.Kind -eq 'Programmability' -and $code -notmatch '^\s*CREATE\s+OR\s+ALTER\s') {
            $errors.Add("${relative}: un obiect programabil începe cu CREATE OR ALTER (MIG-04).")
        }

        foreach ($match in [regex]::Matches($code, '(?i)(?:\[?(?<db>[A-Za-z0-9_]+)\]?\s*\.\s*)?\[?\bdbo\]?\s*\.')) {
            $db = $match.Groups['db'].Value
            if ($db -notin @('msdb', 'master')) {
                $errors.Add("${relative}: referință la dbo. (SQL-01; permis doar msdb.dbo. și master.dbo.).")
                break
            }
        }
    }

    if ($PreviousTag) {
        $changes = & git -C $RepoRoot diff --name-status $PreviousTag -- ':(glob)**/Migrations/*.sql' 2>&1
        if ($LASTEXITCODE -ne 0) { $errors.Add("git diff față de $PreviousTag a eșuat: $changes") }
        foreach ($line in $changes) {
            if ("$line" -match '^(M|D|R\d*)\s+(.+)$') {
                $errors.Add("$($Matches[2]): migrare deja livrată în $PreviousTag a fost modificată sau ștearsă (MIG-02).")
            }
        }
    }

    , $errors
}

function Get-KitMigrations {
    <# Migratiile in ordinea de executie (8.5): prefixul AAAA.LL.NNN, apoi ordinea modulelor. #>
    param([Parameter(Mandatory)][string]$RepoRoot)

    $found = Get-KitDatabaseSources -RepoRoot $RepoRoot
    $items = foreach ($source in $found.Sources) {
        $folder = Join-Path $source.Path 'Migrations'
        if (-not (Test-Path $folder)) { continue }
        Get-ChildItem $folder -Filter *.sql -File | ForEach-Object {
            [pscustomobject]@{
                Schema      = $source.Schema
                Prefix      = $_.Name.Substring(0, [Math]::Min(11, $_.Name.Length))
                ModuleIndex = $script:ModuleOrder.IndexOf($source.Schema)
                Name        = "$($source.Schema)/$($_.Name)"
                File        = $_
            }
        }
    }
    , @($items | Sort-Object -Property @{ Expression = 'Prefix'; Ascending = $true }, @{ Expression = 'ModuleIndex'; Ascending = $true } -CaseSensitive)
}

function Copy-KitDatabaseScripts {
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$KitRoot)

    $bootstrap = Join-Path $RepoRoot 'src/Setup/Erp.Setup.Sql/Database/Bootstrap'
    if (Test-Path $bootstrap) {
        $target = New-Item -ItemType Directory -Force (Join-Path $KitRoot 'database/bootstrap')
        Get-ChildItem $bootstrap -Filter *.sql -File | Copy-Item -Destination $target
    }

    foreach ($source in (Get-KitDatabaseSources -RepoRoot $RepoRoot).Sources) {
        foreach ($kind in @(@{ From = 'Migrations'; To = 'migrations' }, @{ From = 'Programmability'; To = 'programmability' }, @{ From = 'Seed'; To = 'seed' })) {
            $folder = Join-Path $source.Path $kind.From
            if (-not (Test-Path $folder)) { continue }
            foreach ($file in Get-ChildItem $folder -Filter *.sql -File -Recurse) {
                $relative = [IO.Path]::GetRelativePath($folder, $file.FullName)
                $destination = Join-Path $KitRoot "database/$($kind.To)/$($source.Schema)/$relative"
                New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
                Copy-Item $file.FullName $destination
            }
        }
    }
}

function Get-KitScriptVersions {
    <#
    Versiunea fiecarui script SQL din kit (INSTALARE_ACTUALIZARE.md 5.9): de cate ori s-a schimbat fisierul in istoricul Git,
    urmarind redenumirile, plus 1 daca are modificari necomise (un fisier nou necomis are versiunea 1). Cheia e numele din
    jurnal, ca in KitScripts: <schema>/<fisier> pentru migratii si date livrate, <schema>/<Folder>/<fisier> pentru obiectele
    programabile. Aceeasi regula o aplica baza de dezvoltare (src/Tools/Erp.DevDatabase.Sync/GitScriptVersions.cs).
    Cere istoricul complet (checkout cu fetch-depth: 0); intr-un depozit superficial versiunile ar fi prea mici.
    #>
    param([Parameter(Mandatory)][string]$RepoRoot)

    $versions = [ordered]@{}
    # Un depozit fara niciun commit nu are istoric: git log ar esua, deci toate fisierele au 0 commit-uri.
    & git -C $RepoRoot rev-parse --verify -q HEAD 2>$null | Out-Null
    $hasCommits = $LASTEXITCODE -eq 0
    foreach ($source in (Get-KitDatabaseSources -RepoRoot $RepoRoot).Sources) {
        foreach ($kind in 'Migrations', 'Programmability', 'Seed') {
            $folder = Join-Path $source.Path $kind
            if (-not (Test-Path $folder)) { continue }
            foreach ($file in Get-ChildItem $folder -Filter *.sql -File -Recurse | Sort-Object FullName) {
                $name = "$($source.Schema)/" + [IO.Path]::GetRelativePath($folder, $file.FullName).Replace('\', '/')
                $path = [IO.Path]::GetRelativePath($RepoRoot, $file.FullName).Replace('\', '/')
                $commits = @()
                if ($hasCommits) {
                    $commits = @(& git -C $RepoRoot log --follow --format=%H -- $path 2>$null | Where-Object { $_ })
                    if ($LASTEXITCODE -ne 0) { throw "Istoricul Git al fișierului $path nu poate fi citit." }
                }
                $pending = @(& git -C $RepoRoot status --porcelain -- $path 2>$null | Where-Object { $_ })
                $versions[$name] = $commits.Count + $(if ($pending.Count -gt 0) { 1 } else { 0 })
            }
        }
    }
    $versions
}

function Compare-ProductVersion {
    <# Comparatie semantica AAAA.L.patch (GIT-05). #>
    param([Parameter(Mandatory)][string]$Left, [Parameter(Mandatory)][string]$Right)
    ([version]$Left).CompareTo([version]$Right)
}

function Get-KitVersionTags {
    param([Parameter(Mandatory)][string]$RepoRoot)
    $lines = & git -C $RepoRoot for-each-ref refs/tags --format='%(refname:short)|%(creatordate:short)' 2>$null
    foreach ($line in $lines) {
        $name, $date = "$line".Split('|')
        if ($name -match '^v?(\d{4}\.\d+\.\d+)$') {
            [pscustomobject]@{ Tag = $name; Version = $Matches[1]; Date = [datetime]::ParseExact($date, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture) }
        }
    }
}

function Get-KitMinUpgradeFrom {
    <# Cea mai veche versiune livrata in ultimele 12 luni, mai mica decat versiunea curenta; altfel versiunea curenta. #>
    param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][datetime]$ReleaseDate, [object[]]$Tags = @())
    $candidates = @($Tags | Where-Object { $_.Date -ge $ReleaseDate.AddMonths(-12) -and (Compare-ProductVersion $_.Version $Version) -lt 0 } |
        Sort-Object { [version]$_.Version })
    if ($candidates.Count -gt 0) { $candidates[0].Version } else { $Version }
}

function Get-KitPreviousTag {
    param([Parameter(Mandatory)][string]$Version, [object[]]$Tags = @())
    $previous = @($Tags | Where-Object { (Compare-ProductVersion $_.Version $Version) -lt 0 } | Sort-Object { [version]$_.Version } -Descending)
    if ($previous.Count -gt 0) { $previous[0].Tag } else { $null }
}

function Import-KitSigningKey {
    <# Cheia privata ECDSA P-256 (PEM PKCS#8). Daca e criptata, parola se ia din ERP_KIT_SIGNING_KEY_PASSWORD. #>
    param([Parameter(Mandatory)][string]$Path)
    $pem = Get-Content -Raw -LiteralPath $Path
    $key = [Security.Cryptography.ECDsa]::Create()
    if ($pem -match 'ENCRYPTED PRIVATE KEY') {
        if (-not $env:ERP_KIT_SIGNING_KEY_PASSWORD) { throw 'Cheia de semnare este criptată; setați ERP_KIT_SIGNING_KEY_PASSWORD în sesiunea curentă.' }
        $key.ImportFromEncryptedPem($pem, $env:ERP_KIT_SIGNING_KEY_PASSWORD)
    }
    else {
        $key.ImportFromPem($pem)
    }
    Assert-P256 $key
    $key
}

function Assert-P256 {
    param([Parameter(Mandatory)][Security.Cryptography.ECDsa]$Key)
    if ($Key.KeySize -ne 256 -or $Key.ExportParameters($false).Curve.Oid.Value -ne '1.2.840.10045.3.1.7') {
        throw 'Cheia kitului trebuie să fie ECDSA P-256.'
    }
}

function New-KitManifest {
    <# Scrie manifest.json (4.2) cu toate fisierele kitului, apoi manifest.sig (Base64 al semnaturii IEEE P1363). #>
    param(
        [Parameter(Mandatory)][string]$KitRoot,
        [Parameter(Mandatory)][hashtable]$Metadata,
        [Parameter(Mandatory)][Security.Cryptography.ECDsa]$SigningKey
    )

    $root = (Resolve-Path $KitRoot).Path
    $files = Get-ChildItem $root -File -Recurse |
        ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') } |
        Where-Object { $_ -cnotin @('manifest.json', 'manifest.sig') } |
        Sort-Object -CaseSensitive |
        ForEach-Object {
            $full = Join-Path $root $_
            [ordered]@{ path = $_; sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $full).Hash.ToLowerInvariant(); size = (Get-Item -LiteralPath $full).Length }
        }

    $manifest = [ordered]@{
        product                  = 'ERP'
        version                  = $Metadata.Version
        releaseDate              = $Metadata.ReleaseDate
        minUpgradeFrom           = $Metadata.MinUpgradeFrom
        minSqlServerMajorVersion = 16
        sqlCompatibilityLevel    = 160
        databaseCollation        = 'Latin1_General_100_CS_AS_SC_UTF8'
        hostingBundleVersion     = $Metadata.HostingBundleVersion
        lastMigration            = $Metadata.LastMigration
        kitKeyId                 = if ($Metadata.ContainsKey('KitKeyId')) { [int]$Metadata.KitKeyId } else { 1 }
        scriptVersions           = if ($Metadata.ContainsKey('ScriptVersions')) { $Metadata.ScriptVersions } else { $null }
        files                    = @($files)
    }

    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($manifest | ConvertTo-Json -Depth 5))
    [IO.File]::WriteAllBytes((Join-Path $root 'manifest.json'), $bytes)
    $signature = $SigningKey.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
    [IO.File]::WriteAllText((Join-Path $root 'manifest.sig'), [Convert]::ToBase64String($signature))
}

Export-ModuleMember -Function Get-KitModuleOrder, Get-KitDatabaseSources, Test-KitScripts, Get-KitMigrations, Copy-KitDatabaseScripts,
    Get-KitScriptVersions, Compare-ProductVersion, Get-KitVersionTags, Get-KitMinUpgradeFrom, Get-KitPreviousTag, Import-KitSigningKey, Assert-P256, New-KitManifest
