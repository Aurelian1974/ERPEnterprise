using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.State;

namespace Erp.Setup.Tests.Checks;

[Trait("Criteriu", "08-IA-20")]
public sealed class RequirementsCheckerTests : IDisposable
{
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

    private async Task<RequirementsReport> Run(Func<CheckTarget, CheckTarget>? configure = null, InstanceState? existing = null, string? password = null)
    {
        var target = CheckFixtures.Target(License());
        target = configure?.Invoke(target) ?? target;
        var checker = new RequirementsChecker(windows, sql, network, licenses.Reader, TimeProvider.System);
        return await checker.RunAsync(target, CheckFixtures.Manifest, existing, password, TestContext.Current.CancellationToken);
    }

    private static CheckResult Result(RequirementsReport report, string id) => Assert.Single(report.Results, r => r.Id == id);

    [Fact]
    public async Task All_requirements_met_on_a_production_like_server()
    {
        sql.Info = sql.Info with { Collation = "Latin1_General_100_CS_AS_SC_UTF8" };

        var report = await Run();

        Assert.Equal(Enumerable.Range(1, 23).Select(i => $"C-{i:00}"), report.Results.Select(r => r.Id));
        Assert.All(report.Results.Where(r => r.Id is not ("C-16" or "C-23")), r => Assert.Equal(CheckStatus.Passed, r.Status));
        Assert.Equal(CheckStatus.NotApplicable, Result(report, "C-16").Status);
        Assert.Equal(ExitCodes.Success, report.ExitCode);
        Assert.True(sql.Disposed);
        Assert.Empty(sql.CreatedDirectories);
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
    [InlineData(20348, "WinNT", true)]
    [InlineData(19045, "ServerNT", true)]
    [InlineData(20348, "ServerNT", false)]
    public async Task Unsupported_operating_system_fails_C02_with_code_10(int build, string productType, bool is64Bit)
    {
        windows.Os = new OsInfo(build, productType, is64Bit);

        var report = await Run();

        Assert.Equal(CheckStatus.Failed, Result(report, "C-02").Status);
        Assert.Equal(ExitCodes.UnsupportedOsOrNotAdministrator, report.ExitCode);
    }

    [Theory]
    [InlineData(17763, "ServerNT")]
    [InlineData(26100, "LanmanNT")]
    public async Task Supported_windows_server_passes_C02(int build, string productType)
    {
        windows.Os = new OsInfo(build, productType, true);

        Assert.Equal(CheckStatus.Passed, Result(await Run(), "C-02").Status);
    }

    [Theory]
    [InlineData(2, 32, CheckStatus.Failed)]
    [InlineData(4, 6, CheckStatus.Failed)]
    [InlineData(4, 8, CheckStatus.Warning)]
    [InlineData(4, 16, CheckStatus.Passed)]
    public async Task Hardware_thresholds_C03(int cores, int memoryGb, CheckStatus expected)
    {
        windows.LogicalProcessorCount = cores;
        windows.TotalPhysicalMemoryBytes = memoryGb * 1024L * 1024 * 1024 - (memoryGb > 6 ? 200L * 1024 * 1024 : 0);

        Assert.Equal(expected, Result(await Run(), "C-03").Status);
    }

    [Fact]
    public async Task Low_disk_or_missing_volume_fails_C04()
    {
        windows.FreeSpace[@"C:\Program Files\Erp"] = 4L * 1024 * 1024 * 1024;
        windows.FreeSpace[@"C:\ProgramData\Erp"] = null;

        var result = Result(await Run(), "C-04");

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Contains("InstallRoot", result.Message);
        Assert.Contains("DataRoot", result.Message);
    }

    [Fact]
    public async Task Missing_iis_features_warn_when_installation_is_allowed_and_fail_otherwise()
    {
        windows.Features = new HashSet<string> { "Web-Server" };

        var allowed = Result(await Run(), "C-05");
        var forbidden = Result(await Run(t => t with { InstallIisFeatures = false }), "C-05");

        Assert.Equal(CheckStatus.Warning, allowed.Status);
        Assert.Contains("Web-WebSockets", allowed.Message);
        Assert.Equal(CheckStatus.Failed, forbidden.Status);
    }

    [Fact]
    public async Task Features_that_cannot_be_queried_fail_C05()
    {
        windows.Features = null;

        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-05").Status);
    }

    [Theory]
    [InlineData("10.0.2", true, true, CheckStatus.Warning)]
    [InlineData("10.0.2", true, false, CheckStatus.Failed)]
    [InlineData("10.0.5", false, true, CheckStatus.Warning)]
    [InlineData("10.0.3", true, true, CheckStatus.Passed)]
    [InlineData("10.0.5", true, true, CheckStatus.Passed)]
    public async Task Hosting_bundle_version_and_module_C06(string installed, bool module, bool allowInstall, CheckStatus expected)
    {
        windows.AspNetCoreVersions.Clear();
        windows.AspNetCoreVersions.AddRange([Version.Parse(installed), new Version(11, 0, 0)]);
        windows.AspNetCoreModule = module;

        Assert.Equal(expected, Result(await Run(t => t with { InstallHostingBundle = allowInstall }), "C-06").Status);
    }

    [Fact]
    public async Task Expired_certificate_fails_C07()
    {
        windows.Certificate = windows.Certificate! with { NotAfter = DateTimeOffset.UtcNow.AddDays(-1) };

        var result = Result(await Run(), "C-07");

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Contains("a expirat", result.Message);
    }

    [Fact]
    public async Task Certificate_expiring_within_30_days_warns_C07()
    {
        windows.Certificate = windows.Certificate! with { NotAfter = DateTimeOffset.UtcNow.AddDays(10) };

        Assert.Equal(CheckStatus.Warning, Result(await Run(), "C-07").Status);
    }

    [Theory]
    [InlineData(false, true, "erp.grupulexemplu.local", "cheie privată")]
    [InlineData(true, false, "erp.grupulexemplu.local", "Server Authentication")]
    [InlineData(true, true, "alt.grupulexemplu.local", "SAN sau CN")]
    public async Task Invalid_certificate_fails_C07(bool privateKey, bool serverAuth, string dnsName, string expected)
    {
        windows.Certificate = new CertificateInfo(privateKey, DateTimeOffset.UtcNow.AddYears(1), [dnsName], serverAuth);

        var result = Result(await Run(), "C-07");

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Contains(expected, result.Message);
    }

    [Fact]
    public async Task Wildcard_certificate_matches_one_label_C07()
    {
        windows.Certificate = windows.Certificate! with { DnsNames = ["*.grupulexemplu.local"] };

        Assert.Equal(CheckStatus.Passed, Result(await Run(), "C-07").Status);
        Assert.False(RequirementsChecker.DnsNameMatches("*.grupulexemplu.local", "a.erp.grupulexemplu.local"));
    }

    [Fact]
    public async Task Missing_certificate_fails_and_self_signed_without_thumbprint_warns_C07()
    {
        windows.Certificate = null;

        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-07").Status);
        Assert.Equal(CheckStatus.Warning, Result(await Run(t => t with { CertificateThumbprint = null, AllowSelfSignedCertificate = true }), "C-07").Status);
    }

    [Fact]
    public async Task Port_used_by_another_site_with_the_same_host_name_fails_C08()
    {
        windows.Bindings.Add(new IisBinding("Default Web Site", "https", 443, "ERP.grupulexemplu.local"));

        var result = Result(await Run(), "C-08");

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Contains("Default Web Site", result.Message);
        Assert.Equal(ExitCodes.RequirementsNotMet, (await Run()).ExitCode);
    }

    [Fact]
    public async Task Port_80_is_checked_only_with_http_redirect_C08()
    {
        windows.Bindings.Add(new IisBinding("Alt site", "http", 80, "erp.grupulexemplu.local"));

        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-08").Status);
        Assert.Equal(CheckStatus.Passed, Result(await Run(t => t with { HttpRedirect = false }), "C-08").Status);
    }

    [Fact]
    public async Task Other_host_names_and_this_instance_site_do_not_conflict_C08()
    {
        windows.Bindings.Add(new IisBinding("ERP-TEST", "https", 443, "erp-test.grupulexemplu.local"));
        windows.Bindings.Add(new IisBinding("ERP-PROD", "https", 443, "erp.grupulexemplu.local"));
        windows.SniThumbprint = "AAAA9C00112233445566778899AABBCCDDEEFFE1";

        Assert.Equal(CheckStatus.Passed, Result(await Run(), "C-08").Status);
    }

    [Fact]
    public async Task Sni_binding_with_another_certificate_fails_C08()
    {
        windows.SniThumbprint = "AAAA9C00112233445566778899AABBCCDDEEFFE1";

        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-08").Status);
    }

    [Fact]
    public async Task Sql_connection_failure_fails_C09_and_skips_sql_checks()
    {
        sql.ConnectError = "Conexiunea la SQL01\\ERP a eșuat (eroare 18456): Login failed.";

        var report = await Run();

        Assert.Equal(CheckStatus.Failed, Result(report, "C-09").Status);
        foreach (var id in new[] { "C-10", "C-11", "C-12", "C-13", "C-14", "C-15", "C-16", "C-17", "C-18", "C-19" })
        {
            Assert.Equal(CheckStatus.NotApplicable, Result(report, id).Status);
        }

        Assert.Equal(ExitCodes.RequirementsNotMet, report.ExitCode);
    }

    [Fact]
    public async Task Operator_password_is_passed_to_the_connector()
    {
        await Run(password: "p@ss");

        Assert.Equal("p@ss", sql.ReceivedPassword);
    }

    [Fact]
    public async Task Sql_server_2019_fails_C10()
    {
        sql.Info = sql.Info with { MajorVersion = 15 };

        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-10").Status);
    }

    [Theory]
    [InlineData(4, "Express Edition (64-bit)", CheckStatus.Failed)]
    [InlineData(5, "SQL Azure", CheckStatus.Failed)]
    [InlineData(3, "Developer Edition (64-bit)", CheckStatus.Warning)]
    [InlineData(3, "Enterprise Edition (64-bit)", CheckStatus.Passed)]
    [InlineData(2, "Standard Edition (64-bit)", CheckStatus.Passed)]
    public async Task Sql_edition_C11(int engineEdition, string edition, CheckStatus expected)
    {
        sql.Info = sql.Info with { EngineEdition = engineEdition, Edition = edition };

        Assert.Equal(expected, Result(await Run(), "C-11").Status);
    }

    [Theory]
    [InlineData(true, false, false, CheckStatus.Passed)]
    [InlineData(false, true, true, CheckStatus.Failed)]
    [InlineData(false, true, false, CheckStatus.Failed)]
    [InlineData(false, false, true, CheckStatus.Failed)]
    [InlineData(false, false, false, CheckStatus.Failed)]
    public async Task Operator_rights_C12(bool sysadmin, bool dbCreator, bool securityAdmin, CheckStatus expected)
    {
        sql.Info = sql.Info with { IsSysadmin = sysadmin, IsDbCreator = dbCreator, IsSecurityAdmin = securityAdmin };

        Assert.Equal(expected, Result(await Run(), "C-12").Status);
    }

    [Theory]
    [InlineData("Stopped")]
    [InlineData(null)]
    public async Task Stopped_or_unreadable_agent_fails_C13(string? status)
    {
        sql.AgentStatus = status;

        var result = Result(await Run(), "C-13");

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Equal("Porniți SQL Server Agent și setați-l pe Automatic.", result.Message);
    }

    [Fact]
    public async Task Case_insensitive_server_collation_is_a_warning_C14()
    {
        var result = Result(await Run(), "C-14");

        Assert.Equal(CheckStatus.Warning, result.Status);
        Assert.Contains("SQL-23", result.Message);
    }

    [Fact]
    public async Task Existing_database_with_tables_fails_C15_with_code_31()
    {
        sql.Database = new DatabaseInfo(RequirementsChecker.RequiredCollation, 3, null, 0);

        var report = await Run();

        Assert.Equal(CheckStatus.Failed, Result(report, "C-15").Status);
        Assert.Equal(ExitCodes.DatabaseExistsNotEmpty, report.ExitCode);
    }

    [Fact]
    public async Task Existing_empty_database_warns_C15_and_checks_collation_C16()
    {
        sql.Database = new DatabaseInfo("SQL_Latin1_General_CP1_CI_AS", 0, null, 0);

        var report = await Run();

        Assert.Equal(CheckStatus.Warning, Result(report, "C-15").Status);
        Assert.Equal(CheckStatus.Failed, Result(report, "C-16").Status);
        Assert.Equal(ExitCodes.WrongDatabaseCollation, report.ExitCode);
    }

    [Fact]
    public async Task Database_created_by_the_interrupted_installation_is_accepted_C15()
    {
        var installId = Guid.NewGuid();
        sql.Database = new DatabaseInfo(RequirementsChecker.RequiredCollation, 12, installId.ToString(), 0);
        var existing = new InstanceState { Instance = "PROD", InstallId = installId, State = InstanceLifecycle.Installing, InstallRoot = "x", DataRoot = "y" };

        var report = await Run(existing: existing);

        Assert.Equal(CheckStatus.Passed, Result(report, "C-15").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-16").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-23").Status);
    }

    [Fact]
    public async Task Database_of_another_installation_is_refused_on_resume_C15()
    {
        sql.Database = new DatabaseInfo(RequirementsChecker.RequiredCollation, 12, Guid.NewGuid().ToString(), 0);
        var existing = new InstanceState { Instance = "PROD", InstallId = Guid.NewGuid(), State = InstanceLifecycle.Installing, InstallRoot = "x", DataRoot = "y" };

        Assert.Equal(ExitCodes.DatabaseExistsNotEmpty, (await Run(existing: existing)).ExitCode);
    }

    [Fact]
    public async Task Upgrade_requires_the_database_with_the_right_collation_C16()
    {
        sql.Database = new DatabaseInfo("Latin1_General_100_CI_AS_SC_UTF8", 40, null, 0);

        var report = await Run(t => t with { Mode = CheckMode.Upgrade });

        Assert.Equal(CheckStatus.NotApplicable, Result(report, "C-15").Status);
        Assert.Equal(CheckStatus.Failed, Result(report, "C-16").Status);
        Assert.Equal(CheckStatus.NotApplicable, Result(report, "C-23").Status);
        Assert.Equal(ExitCodes.WrongDatabaseCollation, report.ExitCode);
    }

    [Fact]
    public async Task Check_only_reports_missing_sql_folders_C17()
    {
        sql.Directories[@"B:\SQLBackup"] = SqlDirectoryState.WillBeCreated;

        var result = Result(await Run(), "C-17");

        Assert.Equal(CheckStatus.Warning, result.Status);
        Assert.Contains(@"B:\SQLBackup", result.Message);
        Assert.Empty(sql.CreatedDirectories);
    }

    [Fact]
    public async Task Unreachable_sql_folder_fails_C17()
    {
        sql.Directories[@"L:\SQLLog"] = SqlDirectoryState.Unreachable;

        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-17").Status);
    }

    [Fact]
    public async Task Install_creates_sql_folders_C17_and_uses_instance_defaults()
    {
        var result = Result(await Run(t => t with { Mode = CheckMode.Install, DataFilePath = null, LogFilePath = null }), "C-17");

        Assert.Equal(CheckStatus.Passed, result.Status);
        Assert.Equal([@"D:\SQLData\", @"L:\SQLLog\", @"B:\SQLBackup"], sql.CreatedDirectories);
    }

    [Fact]
    public async Task Sql_disk_space_C18()
    {
        sql.FreeSpace = 1024L * 1024 * 1024;
        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-18").Status);

        sql.FreeSpace = null;
        Assert.Equal(CheckStatus.Warning, Result(await Run(), "C-18").Status);
    }

    [Fact]
    public async Task Upgrade_needs_backup_space_for_the_database_C18()
    {
        sql.Database = new DatabaseInfo(RequirementsChecker.RequiredCollation, 40, null, 10L * 1024 * 1024 * 1024);
        sql.FreeSpace = 11L * 1024 * 1024 * 1024;

        var result = Result(await Run(t => t with { Mode = CheckMode.Upgrade }), "C-18");

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Contains("backup", result.Message);
    }

    [Fact]
    public async Task Clock_skew_over_two_minutes_warns_C19()
    {
        sql.Info = sql.Info with { UtcNow = DateTime.UtcNow.AddMinutes(5) };

        Assert.Equal(CheckStatus.Warning, Result(await Run(), "C-19").Status);
    }

    [Fact]
    public async Task Unreachable_anaf_or_bnr_warns_C20()
    {
        network.Unreachable.Add("www.bnr.ro");

        var result = Result(await Run(), "C-20");

        Assert.Equal(CheckStatus.Warning, result.Status);
        Assert.Contains("www.bnr.ro", result.Message);
    }

    [Fact]
    public async Task Disabled_tls12_fails_C21()
    {
        windows.Tls12Disabled = true;

        Assert.Equal(CheckStatus.Failed, Result(await Run(), "C-21").Status);
    }

    [Fact]
    public async Task Expired_license_fails_C22_with_code_40()
    {
        var path = License(TestLicenses.Payload(validUntil: "2020-01-01"));

        var report = await Run(t => t with { LicenseFilePath = path });

        Assert.Equal(CheckStatus.Failed, Result(report, "C-22").Status);
        Assert.Equal(ExitCodes.InvalidLicense, report.ExitCode);
    }

    [Fact]
    public async Task Missing_license_file_fails_C22()
    {
        var report = await Run(t => t with { LicenseFilePath = Path.Combine(Path.GetTempPath(), "lipsa.lic") });

        Assert.Equal(ExitCodes.InvalidLicense, report.ExitCode);
    }

    [Fact]
    public async Task Unknown_license_module_warns_C22()
    {
        var path = License(TestLicenses.Payload(modules: ["Core", "Robotics"]));

        var result = Result(await Run(t => t with { LicenseFilePath = path }), "C-22");

        Assert.Equal(CheckStatus.Warning, result.Status);
        Assert.Contains("Robotics", result.Message);
    }

    [Fact]
    public async Task Existing_instance_fails_C23()
    {
        windows.Sites.Add("ERP-PROD");
        windows.Services.Add("ErpWorker-PROD");
        var existing = new InstanceState { Instance = "PROD", State = InstanceLifecycle.Installed, InstallRoot = "x", DataRoot = "y" };

        var result = Result(await Run(existing: existing), "C-23");

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Contains("ERP-PROD", result.Message);
        Assert.Contains("ErpWorker-PROD", result.Message);
        Assert.Contains("Installed", result.Message);
    }

    [Fact]
    public async Task First_failure_decides_the_exit_code_and_warnings_give_code_1()
    {
        sql.Database = new DatabaseInfo("SQL_Latin1_General_CP1_CI_AS", 0, null, 0);
        windows.Tls12Disabled = true;
        Assert.Equal(ExitCodes.WrongDatabaseCollation, (await Run()).ExitCode);

        sql.Database = null;
        windows.Tls12Disabled = false;
        Assert.Equal(ExitCodes.SuccessWithWarnings, (await Run()).ExitCode);
    }

    [Fact]
    public async Task Probe_exception_is_reported_as_a_failed_check()
    {
        windows.Os = null!;

        var result = Result(await Run(), "C-02");

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.StartsWith("Verificarea nu a putut fi efectuată", result.Message, StringComparison.Ordinal);
    }
}
