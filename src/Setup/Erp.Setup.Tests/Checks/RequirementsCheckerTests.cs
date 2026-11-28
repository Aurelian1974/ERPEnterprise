using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.State;

namespace Erp.Setup.Tests.Checks;

/// <summary>Verificarile C-01…C-20 (08, 5.4) peste servicii simulate.</summary>
[Trait("Criteriu", "08-IA-04")]
public sealed class RequirementsCheckerTests : IDisposable
{
    private const string Collation = RequirementsChecker.RequiredCollation;

    private readonly FakeWindowsProbe windows = new();
    private readonly FakeSqlServer sql = new();
    private readonly FakeNetwork network = new();
    private readonly TestLicenses licenses = new();
    private readonly List<string> tempFiles = [];

    public void Dispose()
    {
        licenses.Dispose();
        tempFiles.ForEach(File.Delete);
    }

    private string License(Erp.Licensing.License? payload = null)
    {
        var path = CheckFixtures.WriteTemp(licenses.Create(payload ?? TestLicenses.Payload()));
        tempFiles.Add(path);
        return path;
    }

    private async Task<RequirementsReport> Run(Func<InstallRequest, InstallRequest>? configure = null, InstanceState? existing = null,
        CheckMode mode = CheckMode.Install, KitCheck? kit = null, string? licensePath = null)
    {
        var request = CheckFixtures.Request(licensePath ?? License());
        request = configure?.Invoke(request) ?? request;
        var checker = new RequirementsChecker(windows, sql, network, licenses.Reader, TimeProvider.System);
        return await checker.RunAsync(CheckTarget.From(request, mode), CheckFixtures.Manifest, kit ?? new KitCheck(true, "ERP 2027.1.0"), existing,
            TestContext.Current.CancellationToken);
    }

    private static CheckResult Result(RequirementsReport report, string id) => Assert.Single(report.Results, r => r.Id == id);

    private static InstallRequest Remote(InstallRequest request, AppIdentity identity, string? gmsa = null) =>
        request with { Sql = request.Sql with { Server = "SQL01", AppIdentity = identity, GmsaAccount = gmsa } };

    [Fact]
    public async Task All_requirements_met_on_a_local_production_server()
    {
        var report = await Run();

        Assert.Equal(Enumerable.Range(1, 20).Select(i => $"C-{i:00}"), report.Results.Select(r => r.Id));
        Assert.All(report.Results, r => Assert.Equal(CheckStatus.Passed, r.Status));
        Assert.Equal(ExitCodes.Success, report.ExitCode);
        Assert.False(report.SqlIsRemote);
        Assert.False(report.TrustServerCertificate);
        Assert.Equal(Path.Combine(@"B:\SQLBackup", "ERP_PROD"), report.BackupFolder);
        Assert.Equal([report.BackupFolder!], sql.CreatedDirectories);
        Assert.True(sql.Disposed);
    }

    [Fact]
    public async Task Not_administrator_fails_C01_with_code_10()
    {
        windows.Administrator = false;

        var report = await Run();

        Assert.Equal(CheckStatus.Failed, Result(report, "C-01").Status);
        Assert.Equal(ExitCodes.UnsupportedOsOrNotAdministrator, report.ExitCode);
    }

    [Theory]
    [InlineData(17763, "ServerNT", true, CheckStatus.Passed)]
    [InlineData(26100, "ServerNT", true, CheckStatus.Passed)]
    [InlineData(22631, "WinNT", true, CheckStatus.Passed)]
    [InlineData(19045, "WinNT", true, CheckStatus.Warning)]
    [InlineData(19044, "WinNT", true, CheckStatus.Failed)]
    [InlineData(14393, "ServerNT", true, CheckStatus.Failed)]
    [InlineData(20348, "ServerNT", false, CheckStatus.Failed)]
    public async Task C02_accepts_Server_2019_and_Windows_11_and_warns_on_Windows_10_22H2(int build, string productType, bool is64Bit, CheckStatus expected)
    {
        windows.Os = new OsInfo(build, productType, is64Bit);

        var report = await Run();

        Assert.Equal(expected, Result(report, "C-02").Status);
        if (expected == CheckStatus.Failed)
        {
            Assert.Equal(ExitCodes.UnsupportedOsOrNotAdministrator, report.ExitCode);
        }
    }

    [Fact]
    public async Task C03_fails_under_4_cores_and_warns_under_16_GB_on_a_server()
    {
        windows.TotalPhysicalMemoryBytes = 8L * 1024 * 1024 * 1024;
        Assert.Equal(CheckStatus.Warning, Result(await Run(), "C-03").Status);

        windows.LogicalProcessorCount = 2;
        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-03").Status);
    }

    [Fact]
    public async Task C04_fails_when_a_local_volume_has_under_5_GB()
    {
        windows.FreeSpace[@"C:\ProgramData\Erp"] = 4L * 1024 * 1024 * 1024;

        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-04").Status);
    }

    [Fact]
    public async Task C05_reports_the_kit_verification_code()
    {
        var report = await Run(kit: new KitCheck(false, "Kitul nu este semnat de producător sau a fost modificat.", ExitCodes.InvalidKitSignature));

        Assert.Equal(CheckStatus.Failed, Result(report, "C-05").Status);
        Assert.Equal(ExitCodes.InvalidKitSignature, report.ExitCode);
    }

    [Fact]
    public async Task C06_and_C07_only_inform_when_IIS_features_or_the_hosting_bundle_are_missing()
    {
        windows.Features = new HashSet<string>(["IIS-WebServerRole"]);
        windows.AspNetCoreModule = false;
        windows.Bindings.Add(new IisBinding("Intranet", "https", 443, "intranet.local"));

        var report = await Run();

        Assert.Equal(CheckStatus.Warning, Result(report, "C-06").Status);
        Assert.Equal(CheckStatus.Warning, Result(report, "C-07").Status);
        Assert.Contains("celelalte 1 site-uri", Result(report, "C-07").Message, StringComparison.Ordinal);
        Assert.False(report.HasFailures);
    }

    [Fact]
    public async Task C08_validates_an_existing_certificate()
    {
        static InstallRequest Existing(InstallRequest r) => r with { Site = r.Site with { Certificate = CertificateKind.Existing, CertificateThumbprint = "AB" } };

        Assert.Equal(CheckStatus.Passed, Result(await Run(Existing), "C-08").Status);

        windows.Certificate = new CertificateInfo(true, DateTimeOffset.UtcNow.AddDays(10), ["*.grupulexemplu.local"], true);
        Assert.Equal(CheckStatus.Warning, Result(await Run(Existing), "C-08").Status);

        windows.Certificate = new CertificateInfo(false, DateTimeOffset.UtcNow.AddYears(1), ["alt.local"], false);
        var failed = Result(await Run(Existing), "C-08");
        Assert.Equal(CheckStatus.Failed, failed.Status);
        Assert.Contains("cheie privată", failed.Message, StringComparison.Ordinal);
        Assert.Contains("SAN", failed.Message, StringComparison.Ordinal);
        Assert.Contains("EKU", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task C09_fails_when_another_site_uses_the_address_on_80_with_redirect()
    {
        windows.Bindings.Add(new IisBinding("Vechi", "http", 80, "erp.grupulexemplu.local"));

        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-09").Status);
        Assert.Equal(CheckStatus.Passed, Result(await Run(r => r with { Site = r.Site with { HttpRedirect = false } }), "C-09").Status);
    }

    [Fact]
    public async Task C10_failure_marks_the_SQL_checks_as_not_run()
    {
        sql.ConnectError = "Conexiunea la SRV-ERP cu contul Windows curent a eșuat.";

        var report = await Run();

        Assert.Equal(CheckStatus.Failed, Result(report, "C-10").Status);
        Assert.All(new[] { "C-11", "C-12", "C-13", "C-14", "C-15", "C-16", "C-18", "C-20" },
            id => Assert.Equal(CheckStatus.NotApplicable, Result(report, id).Status));
        Assert.Equal(ExitCodes.RequirementsNotMet, report.ExitCode);
    }

    [Fact]
    public async Task C10_warns_and_keeps_the_trust_flag_when_the_server_certificate_is_not_trusted()
    {
        sql.TrustedServerCertificate = true;

        var report = await Run();

        Assert.Equal(CheckStatus.Warning, Result(report, "C-10").Status);
        Assert.True(report.TrustServerCertificate);
    }

    [Fact]
    public async Task C11_requires_SQL_2022_and_warns_for_Developer_and_Express()
    {
        sql.Info = sql.Info with { MajorVersion = 15 };
        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-11").Status);

        sql.Info = sql.Info with { MajorVersion = 16, Edition = "Developer Edition (64-bit)" };
        Assert.Equal(CheckStatus.Warning, Result(await Run(), "C-11").Status);

        sql.Info = sql.Info with { Edition = "Express Edition (64-bit)" };
        var express = await Run();
        Assert.Equal(CheckStatus.Warning, Result(express, "C-11").Status);
        Assert.Equal(CheckStatus.NotApplicable, Result(express, "C-13").Status);
        Assert.Equal(CheckStatus.NotApplicable, Result(express, "C-16").Status);
    }

    [Fact]
    public async Task C12_requires_sysadmin_with_code_12()
    {
        sql.Info = sql.Info with { IsSysadmin = false };

        var report = await Run();

        Assert.Equal(CheckStatus.Failed, Result(report, "C-12").Status);
        Assert.Equal(ExitCodes.RequirementsNotMet, report.ExitCode);
    }

    [Fact]
    public async Task C13_fails_when_SQL_Agent_is_stopped()
    {
        sql.AgentStatus = "Stopped";

        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-13").Status);
    }

    [Fact]
    public async Task C14_accepts_a_new_or_an_empty_database_and_refuses_one_with_data_or_another_collation()
    {
        sql.Database = new DatabaseInfo(Collation, 0, null, null, null, null, 8_000_000);
        Assert.Equal(CheckStatus.Passed, Result(await Run(), "C-14").Status);

        sql.Database = new DatabaseInfo(Collation, 12, null, null, null, null, 8_000_000);
        var withData = await Run();
        Assert.Equal(CheckStatus.Failed, Result(withData, "C-14").Status);
        Assert.Equal(ExitCodes.DatabaseExistsNotEmpty, withData.ExitCode);

        sql.Database = new DatabaseInfo("SQL_Latin1_General_CP1_CI_AS", 0, null, null, null, null, 8_000_000);
        Assert.Equal(ExitCodes.WrongDatabaseCollation, (await Run()).ExitCode);
    }

    [Fact]
    public async Task C14_accepts_its_own_database_on_resume_and_refuses_another_installation()
    {
        var installId = Guid.NewGuid();
        sql.Database = new DatabaseInfo(Collation, 300, "ERP", "PROD", installId.ToString(), "core/x.sql", 8_000_000);
        var existing = new InstanceState { Instance = "PROD", State = InstanceLifecycle.Installing, InstallId = installId, InstallRoot = @"C:\Program Files\Erp" };

        Assert.Equal(CheckStatus.Passed, Result(await Run(existing: existing), "C-14").Status);

        var other = await Run();
        Assert.Equal(CheckStatus.Failed, Result(other, "C-14").Status);
        Assert.Contains("Recuperează o instalare existentă", Result(other, "C-14").Message, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.DatabaseExistsNotEmpty, other.ExitCode);
    }

    [Fact]
    public async Task C15_remote_SQL_requires_a_domain_and_a_usable_gMSA_or_warns_for_the_computer_account()
    {
        sql.Info = sql.Info with { MachineName = "SQL01" };

        var gmsa = await Run(r => Remote(r, AppIdentity.Gmsa, @"GRUPULEXEMPLU\erp-prod$"));
        Assert.Equal(CheckStatus.Passed, Result(gmsa, "C-15").Status);
        Assert.True(gmsa.SqlIsRemote);

        Assert.Equal(CheckStatus.Failed, Result(await Run(r => Remote(r, AppIdentity.Gmsa, @"GRUPULEXEMPLU\alt$")), "C-15").Status);
        Assert.Equal(CheckStatus.Warning, Result(await Run(r => Remote(r, AppIdentity.ComputerAccount)), "C-15").Status);
        Assert.Equal(CheckStatus.Failed, Result(await Run(r => Remote(r, AppIdentity.VirtualAccounts)), "C-15").Status);

        windows.Domain = null;
        Assert.Equal(CheckStatus.Failed, Result(await Run(r => Remote(r, AppIdentity.ComputerAccount)), "C-15").Status);
    }

    [Fact]
    public async Task C16_and_C20_create_the_folders_with_the_SQL_Server_account()
    {
        sql.FailingDirectories.Add(@"E:\Backup");
        var report = await Run(r => r with
        {
            Backup = r.Backup with { Folder = @"E:\Backup" },
            Sql = r.Sql with { DataFolder = @"D:\Date", LogFolder = @"L:\Log" },
        });

        Assert.Equal(CheckStatus.Failed, Result(report, "C-16").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-20").Status);
        Assert.Contains(@"D:\Date", sql.CreatedDirectories);
        Assert.Contains(@"L:\Log", sql.CreatedDirectories);
    }

    [Fact]
    public async Task C17_refuses_an_invalid_license_and_one_without_updates_for_the_kit()
    {
        var path = CheckFixtures.WriteTemp([1, 2, 3]);
        tempFiles.Add(path);
        var invalid = await Run(licensePath: path);
        Assert.Equal(CheckStatus.Failed, Result(invalid, "C-17").Status);
        Assert.Equal(ExitCodes.InvalidLicense, invalid.ExitCode);

        var noUpdates = await Run(licensePath: License(TestLicenses.Payload() with { UpdatesUntil = new DateOnly(2026, 12, 31) }));
        Assert.Equal(CheckStatus.Failed, Result(noUpdates, "C-17").Status);
        Assert.Equal(ExitCodes.LicenseExcludesKit, noUpdates.ExitCode);
    }

    [Fact]
    public async Task C18_and_C19_only_warn()
    {
        sql.Info = sql.Info with { UtcNow = DateTime.UtcNow.AddMinutes(-5) };
        network.Unreachable.Add("api.anaf.ro");

        var report = await Run();

        Assert.Equal(CheckStatus.Warning, Result(report, "C-18").Status);
        Assert.Equal(CheckStatus.Warning, Result(report, "C-19").Status);
        Assert.Equal(ExitCodes.SuccessWithWarnings, report.ExitCode);
        Assert.Equal(["C-18", "C-19"], report.UnacceptedWarnings(new HashSet<string>()).Select(w => w.Id));
        Assert.Empty(report.UnacceptedWarnings(new HashSet<string>(["C-18", "C-19"])));
    }

    [Fact]
    public async Task C19_probes_ANAF_including_the_VAT_web_service_and_BNR()
    {
        var report = await Run();

        Assert.Equal(CheckStatus.Passed, Result(report, "C-19").Status);
        Assert.Equal(
            new[]
            {
                (HttpMethod.Head, "https://api.anaf.ro/"),
                (HttpMethod.Get, "https://curs.bnr.ro/nbrfxrates.xml"),
                (HttpMethod.Head, "https://webservicesp.anaf.ro/"),
            },
            network.Requests.Select(r => (r.Method, r.Uri.AbsoluteUri)).OrderBy(r => r.AbsoluteUri, StringComparer.Ordinal));
        Assert.All(network.Requests, r => Assert.Equal(TimeSpan.FromSeconds(10), r.Timeout));
    }

    [Fact]
    public async Task C19_warns_and_names_the_VAT_web_service_when_only_it_is_unreachable()
    {
        network.Unreachable.Add("webservicesp.anaf.ro");

        var report = await Run();

        var c19 = Result(report, "C-19");
        Assert.Equal(CheckStatus.Warning, c19.Status);
        Assert.Contains("webservicesp.anaf.ro", c19.Message);
        Assert.DoesNotContain("api.anaf.ro", c19.Message);
        Assert.DoesNotContain("curs.bnr.ro", c19.Message);
        Assert.Equal(ExitCodes.SuccessWithWarnings, report.ExitCode);
    }

    [Fact]
    public async Task Upgrade_requires_the_ERP_database_and_does_not_read_the_license_file()
    {
        var missing = await Run(mode: CheckMode.Upgrade, licensePath: "nu-exista.lic");
        Assert.Equal(CheckStatus.Failed, Result(missing, "C-14").Status);
        Assert.Equal(CheckStatus.NotApplicable, Result(missing, "C-17").Status);

        sql.Database = new DatabaseInfo(Collation, 300, "ERP", "PROD", Guid.NewGuid().ToString(), "core/x.sql", 8_000_000);
        Assert.Equal(CheckStatus.Passed, Result(await Run(mode: CheckMode.Upgrade), "C-14").Status);
    }
}
