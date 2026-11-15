#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'KitBuilder.psm1') -Force

    function New-FakeRepo {
        $root = Join-Path ([IO.Path]::GetTempPath()) ("erp-kit-repo-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force $root | Out-Null
        $root
    }

    function Add-Sql {
        param([string]$Root, [string]$RelativePath, [string]$Content, [switch]$Bom)
        $path = Join-Path $Root $RelativePath
        New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
        [IO.File]::WriteAllText($path, $Content, [Text.UTF8Encoding]::new([bool]$Bom))
    }

    $script:core = 'src/Modules/Core/Erp.Core.Infrastructure/Database'
    $script:sales = 'src/Modules/Sales/Erp.Sales.Infrastructure/Database'
}

Describe 'Test-KitScripts' {
    BeforeEach { $repo = New-FakeRepo }
    AfterEach { Remove-Item $repo -Recurse -Force }

    It 'accepta scripturi corecte' {
        Add-Sql $repo "$core/Migrations/2026.11.001_Create_Company.sql" "CREATE TABLE core.Company (Id int NOT NULL);"
        Add-Sql $repo "$core/Programmability/Procedures/core.usp_Company_Get.sql" "-- comentariu`n/* bloc */`nCREATE OR ALTER PROCEDURE core.usp_Company_Get AS SELECT 1;"
        Add-Sql $repo "$core/Seed/01_Currencies.sql" "SELECT b.backup_finish_date FROM msdb.dbo.backupset AS b; EXEC master.dbo.xp_create_subdir N'B:\x';"
        Add-Sql $repo 'src/Setup/Erp.Setup.Sql/Database/Bootstrap/001_Schemas.sql' "CREATE SCHEMA core AUTHORIZATION dbo;"

        Test-KitScripts -RepoRoot $repo | Should -BeNullOrEmpty
    }

    It 'refuza numele de migrare care nu respecta MIG-01' {
        Add-Sql $repo "$core/Migrations/2026.11.1_Create_Company.sql" "SELECT 1;"
        Add-Sql $repo "$core/Migrations/2026.11.002_Create-Company.sql" "SELECT 1;"

        $errors = Test-KitScripts -RepoRoot $repo
        $errors.Count | Should -Be 2
        $errors | Should -Match 'MIG-01'
    }

    It 'refuza doua migrari cu acelasi prefix in acelasi modul, dar le accepta in module diferite' {
        Add-Sql $repo "$core/Migrations/2026.11.001_A.sql" "SELECT 1;"
        Add-Sql $repo "$core/Migrations/2026.11.001_B.sql" "SELECT 1;"
        Add-Sql $repo "$sales/Migrations/2026.11.001_C.sql" "SELECT 1;"

        $errors = Test-KitScripts -RepoRoot $repo
        $errors.Count | Should -Be 1
        $errors[0] | Should -Match 'prefixul 2026.11.001'
    }

    It 'cere CREATE OR ALTER la obiectele programabile' {
        Add-Sql $repo "$core/Programmability/Views/core.v_Company.sql" "CREATE VIEW core.v_Company AS SELECT 1 AS Id;"

        Test-KitScripts -RepoRoot $repo | Should -Match 'CREATE OR ALTER'
    }

    It 'refuza referintele la dbo. in afara msdb si master: <Sql>' -ForEach @(
        @{ Sql = 'SELECT * FROM dbo.Company;' }
        @{ Sql = 'SELECT * FROM [dbo].[Company];' }
        @{ Sql = 'EXEC ERP_PROD.dbo.usp_X;' }
    ) {
        Add-Sql $repo "$core/Migrations/2026.11.001_A.sql" $Sql

        Test-KitScripts -RepoRoot $repo | Should -Match 'dbo\.'
    }

    It 'ignora dbo. din comentarii' {
        Add-Sql $repo "$core/Migrations/2026.11.001_A.sql" "-- nu folosim dbo.Company`nSELECT 1;"

        Test-KitScripts -RepoRoot $repo | Should -BeNullOrEmpty
    }

    It 'refuza fisierele cu BOM sau care nu sunt UTF-8' {
        Add-Sql $repo "$core/Migrations/2026.11.001_A.sql" "SELECT N'ă';" -Bom
        $invalid = Join-Path $repo "$core/Migrations/2026.11.002_B.sql"
        [IO.File]::WriteAllBytes($invalid, [byte[]](0x53, 0xE3, 0x0A))

        $errors = (Test-KitScripts -RepoRoot $repo) -join "`n"
        $errors | Should -Match 'BOM'
        $errors | Should -Match 'UTF-8 valid'
    }

    It 'refuza un modul fara mapare spre schema' {
        Add-Sql $repo 'src/Modules/Unknown/Erp.Unknown.Infrastructure/Database/Migrations/2026.11.001_A.sql' 'SELECT 1;'

        Test-KitScripts -RepoRoot $repo | Should -Match 'Modul necunoscut'
    }

    It 'refuza o migrare livrata modificata sau stearsa (MIG-02)' {
        Add-Sql $repo "$core/Migrations/2026.11.001_A.sql" 'SELECT 1;'
        Add-Sql $repo "$core/Migrations/2026.11.002_B.sql" 'SELECT 2;'
        git -C $repo init -q
        git -C $repo add -A
        git -C $repo -c user.name=t -c user.email=t@t commit -q -m init
        git -C $repo tag v2026.11.0
        Add-Sql $repo "$core/Migrations/2026.11.001_A.sql" 'SELECT 10;'
        Remove-Item (Join-Path $repo "$core/Migrations/2026.11.002_B.sql")
        Add-Sql $repo "$core/Migrations/2026.11.003_C.sql" 'SELECT 3;'

        $errors = Test-KitScripts -RepoRoot $repo -PreviousTag v2026.11.0
        $errors.Count | Should -Be 2
        $errors | Should -Match 'MIG-02'
    }
}

Describe 'Get-KitMigrations' {
    BeforeEach { $repo = New-FakeRepo }
    AfterEach { Remove-Item $repo -Recurse -Force }

    It 'ordoneaza dupa prefix, apoi dupa ordinea modulelor' {
        Add-Sql $repo "$sales/Migrations/2026.11.001_Sales.sql" 'SELECT 1;'
        Add-Sql $repo "$core/Migrations/2026.12.001_Later.sql" 'SELECT 1;'
        Add-Sql $repo "$core/Migrations/2026.11.001_Core.sql" 'SELECT 1;'
        Add-Sql $repo 'src/Auth/Erp.Auth/Database/Migrations/2026.11.001_Ids.sql' 'SELECT 1;'
        Add-Sql $repo 'src/Worker/Erp.Worker/Database/Migrations/2026.11.001_Hangfire.sql' 'SELECT 1;'

        (Get-KitMigrations -RepoRoot $repo).Name | Should -Be @(
            'core/2026.11.001_Core.sql'
            'ids/2026.11.001_Ids.sql'
            'hangfire/2026.11.001_Hangfire.sql'
            'sales/2026.11.001_Sales.sql'
            'core/2026.12.001_Later.sql'
        )
    }
}

Describe 'Copy-KitDatabaseScripts' {
    It 'copiaza scripturile in structura kitului, pe scheme' {
        $repo = New-FakeRepo
        $kit = Join-Path $repo 'kit'
        Add-Sql $repo 'src/Setup/Erp.Setup.Sql/Database/Bootstrap/001_Schemas.sql' 'SELECT 1;'
        Add-Sql $repo "$sales/Migrations/2026.11.001_A.sql" 'SELECT 1;'
        Add-Sql $repo "$sales/Programmability/Procedures/sales.usp_X.sql" 'CREATE OR ALTER PROCEDURE sales.usp_X AS SELECT 1;'
        Add-Sql $repo "$sales/Seed/01_Data.sql" 'SELECT 1;'
        Add-Sql $repo 'src/Auth/Erp.Auth/Database/Migrations/2026.11.001_Ids.sql' 'SELECT 1;'

        Copy-KitDatabaseScripts -RepoRoot $repo -KitRoot $kit

        foreach ($expected in 'database/bootstrap/001_Schemas.sql', 'database/migrations/sales/2026.11.001_A.sql',
            'database/programmability/sales/Procedures/sales.usp_X.sql', 'database/seed/sales/01_Data.sql', 'database/migrations/ids/2026.11.001_Ids.sql') {
            Join-Path $kit $expected | Should -Exist
        }
        Remove-Item $repo -Recurse -Force
    }
}

Describe 'Versiuni' {
    BeforeAll {
        $tags = @(
            [pscustomobject]@{ Tag = 'v2025.3.0'; Version = '2025.3.0'; Date = [datetime]'2025-09-01' }
            [pscustomobject]@{ Tag = 'v2026.1.0'; Version = '2026.1.0'; Date = [datetime]'2026-02-01' }
            [pscustomobject]@{ Tag = 'v2026.2.0'; Version = '2026.2.0'; Date = [datetime]'2026-06-01' }
            [pscustomobject]@{ Tag = 'v2026.10.0'; Version = '2026.10.0'; Date = [datetime]'2026-12-01' }
        )
    }

    It 'minUpgradeFrom = cea mai veche versiune din ultimele 12 luni' {
        Get-KitMinUpgradeFrom -Version '2027.1.0' -ReleaseDate ([datetime]'2027-01-20') -Tags $tags | Should -Be '2026.1.0'
    }

    It 'minUpgradeFrom = versiunea curenta cand nu exista versiuni anterioare' {
        Get-KitMinUpgradeFrom -Version '2026.0.1' -ReleaseDate ([datetime]'2026-11-15') -Tags @() | Should -Be '2026.0.1'
    }

    It 'tagul anterior foloseste comparatia semantica, nu alfabetica' {
        Get-KitPreviousTag -Version '2027.1.0' -Tags $tags | Should -Be 'v2026.10.0'
    }
}

Describe 'New-KitManifest' {
    It 'listeaza toate fisierele cu SHA-256 si semneaza manifestul exact' {
        $kit = New-FakeRepo
        Add-Sql $kit 'app/host/Erp.Host.dll' 'host'
        Add-Sql $kit 'templates/answers.example.json' '{}'
        $key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)

        New-KitManifest -KitRoot $kit -SigningKey $key -Metadata @{ Version = '2026.0.1'; ReleaseDate = '2026-11-15'; MinUpgradeFrom = '2026.0.1'; HostingBundleVersion = '10.0.0'; LastMigration = $null }

        $bytes = [IO.File]::ReadAllBytes((Join-Path $kit 'manifest.json'))
        $signature = [Convert]::FromBase64String((Get-Content -Raw (Join-Path $kit 'manifest.sig')))
        $key.VerifyData($bytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation) | Should -BeTrue
        $manifest = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
        $manifest.files.path | Should -Be @('app/host/Erp.Host.dll', 'templates/answers.example.json')
        $manifest.files[0].sha256 | Should -Be (Get-FileHash (Join-Path $kit 'app/host/Erp.Host.dll')).Hash.ToLowerInvariant()
        $manifest.databaseCollation | Should -BeExactly 'Latin1_General_100_CS_AS_SC_UTF8'
        $key.Dispose()
        Remove-Item $kit -Recurse -Force
    }

    It 'refuza o cheie care nu este P-256' {
        $key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP384)
        { Assert-P256 $key } | Should -Throw '*P-256*'
        $key.Dispose()
    }
}
