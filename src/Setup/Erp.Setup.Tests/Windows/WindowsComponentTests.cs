using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Erp.Configuration;
using Erp.Setup.Core;
using Erp.Setup.Core.Deployment;
using Erp.Setup.Core.Kit;
using Erp.Setup.Windows.Iis;
using Erp.Setup.Windows.Layout;
using Erp.Setup.Windows.Processes;
using Erp.Setup.Windows.Security;
using Erp.Setup.Windows.Services;

namespace Erp.Setup.Tests.Windows;

public sealed class DeploymentGeneratorTests
{
    private static InstallationSettingsValues Values(string authentication = "Sql", string? login = "erp_prod_app") =>
        new("PROD", "2027.1.0", "https://erp.grupulexemplu.local", @"C:\ProgramData\Erp\PROD", @"SQL01\ERP", "ERP_PROD", authentication, login, "Mandatory", false);

    private static string Template => File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot(), "src", "Setup", "Erp.Setup", "Kit", "templates", "appsettings.Installation.json"));

    [Fact]
    public void Appsettings_for_a_new_installation_have_the_documented_values_and_template_defaults()
    {
        var json = JsonNode.Parse(AppSettingsGenerator.Generate(Template, null, Values()))!;

        Assert.Equal("PROD", (string?)json["Erp"]!["Instance"]);
        Assert.Equal("2027.1.0", (string?)json["Erp"]!["Version"]);
        Assert.Equal(@"C:\ProgramData\Erp\PROD\license\erp.lic", (string?)json["Erp"]!["LicensePath"]);
        Assert.Equal(@"C:\ProgramData\Erp\PROD\maintenance\maintenance.flag", (string?)json["Erp"]!["MaintenanceFlagPath"]);
        Assert.Equal(@"C:\ProgramData\Erp\PROD\keys", (string?)json["Erp"]!["DataProtectionKeysPath"]);
        Assert.Equal("erp_prod_app", (string?)json["Database"]!["UserId"]);
        Assert.Equal(30, (int?)json["Database"]!["CommandTimeoutSeconds"]);
        Assert.Equal(30, (int?)json["Auth"]!["IdleTimeoutMinutes"]);
        Assert.Equal("Database", (string?)json["Attachments"]!["StorageKind"]);
        Assert.Equal(@"C:\ProgramData\Erp\PROD\logs", (string?)json["Logging"]!["Path"]);
        Assert.DoesNotContain("Password", AppSettingsGenerator.Generate(Template, null, Values()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Upgrade_keeps_existing_values_adds_new_template_keys_and_updates_the_version()
    {
        var existing = """
            { "Erp": { "Version": "2026.4.2", "Custom": "pastrat" }, "Auth": { "IdleTimeoutMinutes": 15 }, "Logging": { "RetainedDays": 60 } }
            """;
        var template = Template.Replace("\"Attachments\"", "\"NewSection\": { \"Enabled\": true }, \"Attachments\"", StringComparison.Ordinal);

        var json = JsonNode.Parse(AppSettingsGenerator.Generate(template, existing, Values()))!;

        Assert.Equal("2027.1.0", (string?)json["Erp"]!["Version"]);
        Assert.Equal("pastrat", (string?)json["Erp"]!["Custom"]);
        Assert.Equal(15, (int?)json["Auth"]!["IdleTimeoutMinutes"]);
        Assert.Equal(10, (int?)json["Auth"]!["AbsoluteTimeoutHours"]);
        Assert.Equal(60, (int?)json["Logging"]!["RetainedDays"]);
        Assert.True((bool?)json["NewSection"]!["Enabled"]);
    }

    [Fact]
    public void Windows_authentication_has_no_sql_user()
    {
        var json = JsonNode.Parse(AppSettingsGenerator.Generate(Template, null, Values("Windows", null)))!;

        Assert.Equal("Windows", (string?)json["Database"]!["Authentication"]);
        Assert.Null(json["Database"]!["UserId"]);
    }

    [Theory]
    [InlineData("erp.firma.ro", 443, "https://erp.firma.ro")]
    [InlineData("erp.firma.ro", 8443, "https://erp.firma.ro:8443")]
    public void Public_origin_omits_the_default_port(string host, int port, string expected)
    {
        Assert.Equal(expected, AppSettingsGenerator.PublicOrigin(host, port));
    }

    [Fact]
    public void Web_config_matches_the_specification()
    {
        var xml = WebConfigGenerator.Generate(@"C:\ProgramData\Erp\R&D");
        var document = XDocument.Parse(xml);

        var host = document.Root!.Elements("location").Single(l => (string?)l.Attribute("path") == ".");
        Assert.Equal("false", (string?)host.Attribute("inheritInChildApplications"));
        var aspNetCore = host.Element("system.webServer")!.Element("aspNetCore")!;
        Assert.Equal(@".\Erp.Host.exe", (string?)aspNetCore.Attribute("processPath"));
        Assert.Equal("inprocess", (string?)aspNetCore.Attribute("hostingModel"));
        Assert.Equal("false", (string?)aspNetCore.Attribute("stdoutLogEnabled"));
        var variables = aspNetCore.Element("environmentVariables")!.Elements().ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Attribute("value")!);
        Assert.Equal("Production", variables["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal(@"C:\ProgramData\Erp\R&D", variables["ERP_INSTANCE_DIR"]);
        Assert.Contains("R&amp;D", xml);
        Assert.Equal("52428800", (string?)host.Descendants("requestLimits").Single().Attribute("maxAllowedContentLength"));
        Assert.Equal("X-Powered-By", (string?)host.Descendants("remove").Single().Attribute("name"));

        var windows = document.Root.Elements("location").Single(l => (string?)l.Attribute("path") == "account/windows");
        Assert.Equal("false", (string?)windows.Descendants("anonymousAuthentication").Single().Attribute("enabled"));
        Assert.Equal("true", (string?)windows.Descendants("windowsAuthentication").Single().Attribute("enabled"));
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml, StringComparison.Ordinal);
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsComponentTests
{
    private sealed class FakeRunner : IProcessRunner
    {
        public List<(string File, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public Func<IReadOnlyList<string>, int> ExitCode { get; set; } = _ => 0;

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((Path.GetFileName(fileName), arguments));
            return new ProcessResult(ExitCode(arguments), "ieșire");
        }
    }

    private static readonly WorkerServiceSpec Service = new("PROD", @"C:\Program Files\Erp\PROD\versions\2027.1.0\worker\Erp.Worker.exe", @"C:\ProgramData\Erp\PROD");

    [Fact]
    public void Service_commands_match_section_8_12()
    {
        Assert.Equal(
            ["create", "ErpWorker-PROD", "binPath=", "\"C:\\Program Files\\Erp\\PROD\\versions\\2027.1.0\\worker\\Erp.Worker.exe\" --instance-dir \"C:\\ProgramData\\Erp\\PROD\"",
             "start=", "delayed-auto", "obj=", @"NT SERVICE\ErpWorker-PROD", "DisplayName=", "ERP Worker (PROD)"],
            ServiceCommands.Create(Service));
        Assert.Equal(["failure", "ErpWorker-PROD", "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/300000"], ServiceCommands.Failure(Service));
        Assert.Equal(["failureflag", "ErpWorker-PROD", "1"], ServiceCommands.FailureFlag(Service));
        Assert.Equal(["description", "ErpWorker-PROD", ServiceCommands.Description], ServiceCommands.SetDescription(Service));
        Assert.Equal("config", ServiceCommands.SetBinaryPath(Service)[0]);
    }

    [Fact]
    public void Gmsa_service_uses_the_account_with_an_empty_password()
    {
        var args = ServiceCommands.Create(Service with { WindowsAccount = @"CONTOSO\svc-erp$" });

        Assert.Equal(@"CONTOSO\svc-erp$", args[args.ToList().IndexOf("obj=") + 1]);
        Assert.Equal(string.Empty, args[args.ToList().IndexOf("password=") + 1]);
    }

    [Theory]
    [InlineData(false, "create")]
    [InlineData(true, "config")]
    public void Service_is_created_or_reconfigured(bool exists, string expected)
    {
        var runner = new FakeRunner();

        new WorkerServiceInstaller(runner, _ => exists).Install(Service);

        Assert.Equal([expected, "description", "failure", "failureflag"], runner.Calls.Select(c => c.Arguments[0]));
        Assert.All(runner.Calls, c => Assert.Equal("sc.exe", c.File));
    }

    [Fact]
    public void Failed_sc_command_is_an_iis_service_error_with_code_50()
    {
        var runner = new FakeRunner { ExitCode = a => a[0] == "failure" ? 1060 : 0 };

        var ex = Assert.Throws<WindowsSetupException>(() => new WorkerServiceInstaller(runner, _ => false).Install(Service));

        Assert.Equal(ExitCodes.IisServiceOrFileError, ex.ExitCode);
        Assert.Contains("1060", ex.Message);
    }

    [Fact]
    public void Firewall_rules_are_replaced_by_name_on_domain_and_private_profiles()
    {
        var runner = new FakeRunner { ExitCode = a => a[2] == "delete" ? 1 : 0 };

        new FirewallRules(runner).Apply("PROD", 443, httpRedirect: true);

        Assert.Equal(
            ["delete ERP PROD HTTPS", "add ERP PROD HTTPS", "delete ERP PROD HTTP", "add ERP PROD HTTP"],
            runner.Calls.Select(c => $"{c.Arguments[2]} {c.Arguments[4]["name=".Length..]}"));
        Assert.Equal(["advfirewall", "firewall", "add", "rule", "name=ERP PROD HTTPS", "dir=in", "action=allow", "protocol=TCP", "localport=443", "profile=domain,private"],
            runner.Calls[1].Arguments);
        Assert.Equal("localport=80", runner.Calls[3].Arguments[8]);
    }

    [Fact]
    public void Without_redirect_the_http_rule_is_removed()
    {
        var runner = new FakeRunner();

        new FirewallRules(runner).Apply("PROD", 443, httpRedirect: false);

        Assert.Equal("delete", runner.Calls[^1].Arguments[2]);
        Assert.Equal("name=ERP PROD HTTP", runner.Calls[^1].Arguments[4]);
    }

    [Fact]
    public void Failed_firewall_rule_is_an_error()
    {
        var runner = new FakeRunner { ExitCode = a => a[2] == "add" ? 1 : 0 };

        Assert.Throws<WindowsSetupException>(() => new FirewallRules(runner).Apply("PROD", 443, false));
    }

    [Fact]
    public void Self_signed_certificate_is_valid_one_year_for_the_host_name_with_server_authentication()
    {
        var now = DateTimeOffset.UtcNow;

        using var certificate = SelfSignedCertificate.Create("erp-test.firma.local", now);

        Assert.Equal("CN=erp-test.firma.local", certificate.Subject);
        Assert.True(certificate.HasPrivateKey);
        Assert.InRange(certificate.NotAfter.ToUniversalTime(), now.AddYears(1).AddMinutes(-1).UtcDateTime, now.AddYears(1).AddMinutes(1).UtcDateTime);
        Assert.Equal(["erp-test.firma.local"], certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single().EnumerateDnsNames());
        Assert.Contains(certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single().EnhancedKeyUsages.Cast<Oid>(),
            o => o.Value == SelfSignedCertificate.ServerAuthenticationEku);
    }

    [Theory]
    [InlineData(80, "TrustedInstaller", "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464")]
    [InlineData(82, "DefaultAppPool", "S-1-5-82-3006700770-424185619-1745488364-794895919-4004696415")]
    public void Virtual_account_sids_are_computed_like_windows(int authority, string name, string expected)
    {
        Assert.Equal(expected, VirtualSid(authority, name));
    }

    [Fact]
    public void Computed_service_sid_matches_the_sid_resolved_by_windows()
    {
        Assert.Equal(ResolvedSid(@"NT SERVICE\TrustedInstaller"), VirtualSid(80, "TrustedInstaller"));
    }

    [Fact]
    public void Component_identities_use_the_pool_and_service_names_of_the_instance()
    {
        Assert.Equal((VirtualSid(82, "ERP-PROD-Host"), VirtualSid(80, "ErpWorker-PROD")), Identities("PROD"));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string VirtualSid(int authority, string name) => ComponentIdentities.VirtualAccountSid(authority, name).Value;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string ResolvedSid(string account) => ((SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier))).Value;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static (string, string) Identities(string instance)
    {
        var identities = ComponentIdentities.For(instance);
        return (identities.HostPool.Value, identities.Worker.Value);
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsFileSystemTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "erp-win-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private InstanceLayout Layout() => new(Path.Combine(root, "Program Files", "Erp"), Path.Combine(root, "ProgramData", "Erp"), "PROD");

    [Fact]
    public void Secrets_are_dpapi_protected_with_per_instance_entropy()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, InstallationSecrets.FileName);
        const string password = "Secret#Parola_SQL_1234567890abcdef";

        InstallationSecrets.Write(path, new Dictionary<string, string> { [InstallationSecrets.DatabasePasswordKey] = password }, "PROD");

        Assert.Equal(password, InstallationSecrets.Read(path, "PROD")[InstallationSecrets.DatabasePasswordKey]);
        Assert.DoesNotContain(password, System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        Assert.ThrowsAny<CryptographicException>(() => InstallationSecrets.Read(path, "TEST"));
    }

    [Fact]
    public void Data_folders_are_protected_and_components_get_only_their_rights()
    {
        var layout = Layout();
        layout.CreateDataFolders();
        File.WriteAllText(layout.Data.InstanceJson, "{}");
        var identities = ComponentIdentities.For("PROD");
        using var current = WindowsIdentity.GetCurrent();
        var owners = NtfsPermissions.DefaultOwners.Append(current.User!).ToList();

        NtfsPermissions.ApplyData(layout, identities, owners);
        NtfsPermissions.ApplyData(layout, identities, owners);

        var rootSecurity = new DirectoryInfo(layout.Data.InstanceDirectory).GetAccessControl();
        Assert.True(rootSecurity.AreAccessRulesProtected);
        Assert.Equal(owners.Select(o => o.Value).Order(), Rules(layout.Data.InstanceDirectory).Select(r => r.IdentityReference.Value).Order());

        Assert.True(Has(layout.Data.ConfigDirectory, identities.HostPool, FileSystemRights.ReadAndExecute));
        Assert.True(Has(layout.Data.ConfigDirectory, identities.Worker, FileSystemRights.ReadAndExecute));
        Assert.True(Has(layout.Data.LicenseDirectory, identities.Worker, FileSystemRights.ReadAndExecute));
        Assert.True(Has(layout.Data.MaintenanceDirectory, identities.HostPool, FileSystemRights.ReadAndExecute));
        Assert.True(Has(layout.Data.KeysDirectory, identities.HostPool, FileSystemRights.Modify));
        Assert.False(Has(layout.Data.KeysDirectory, identities.Worker, FileSystemRights.ReadData));
        Assert.True(Has(layout.Data.HostLogs, identities.HostPool, FileSystemRights.Modify));
        Assert.True(Has(layout.Data.WorkerLogs, identities.Worker, FileSystemRights.Modify));
        Assert.False(Has(layout.Data.UpgradeDirectory, identities.HostPool, FileSystemRights.ReadData));
        Assert.False(Has(layout.Data.InstallDirectory, identities.Worker, FileSystemRights.ReadData));
        Assert.False(Has(layout.Data.SetupLogs, identities.HostPool, FileSystemRights.ReadData));
        Assert.True(Has(layout.Data.InstanceJson, identities.HostPool, FileSystemRights.Read));
        Assert.Single(Rules(layout.Data.KeysDirectory), r => r.IdentityReference == identities.HostPool && !r.IsInherited);
    }

    [Fact]
    public void Binaries_are_copied_verified_and_not_copied_again()
    {
        var kit = Path.Combine(root, "kit");
        var files = new List<KitFile>
        {
            AddKitFile(kit, "app/host/Erp.Host.dll", "host-binary"),
            AddKitFile(kit, "app/host/wwwroot/index.html", "<html></html>"),
            AddKitFile(kit, "app/worker/Erp.Worker.exe", "worker-binary"),
            AddKitFile(kit, "templates/answers.example.json", "{}"),
        };
        var manifest = new KitManifest
        {
            Product = "ERP", Version = "2027.1.0", ReleaseDate = "2027-01-20", MinUpgradeFrom = "2026.2.0", MinSqlServerMajorVersion = 16,
            SqlCompatibilityLevel = 160, DatabaseCollation = "Latin1_General_100_CS_AS_SC_UTF8", HostingBundleVersion = "10.0.3", Files = files,
        };
        var layout = Layout();

        Assert.Equal(3, BinaryDeployer.Deploy(kit, manifest, layout, replaceExisting: false));
        Assert.Equal("host-binary", File.ReadAllText(Path.Combine(layout.HostDirectory("2027.1.0"), "Erp.Host.dll")));
        Assert.True(File.Exists(Path.Combine(layout.HostDirectory("2027.1.0"), "wwwroot", "index.html")));
        Assert.True(File.Exists(Path.Combine(layout.WorkerDirectory("2027.1.0"), "Erp.Worker.exe")));
        Assert.False(Directory.Exists(Path.Combine(layout.VersionsDirectory, "2027.1.0", "templates")));

        Assert.Equal(0, BinaryDeployer.Deploy(kit, manifest, layout, replaceExisting: false));

        File.WriteAllText(Path.Combine(layout.WorkerDirectory("2027.1.0"), "Erp.Worker.exe"), "modificat");
        Assert.Equal(1, BinaryDeployer.Deploy(kit, manifest, layout, replaceExisting: false));

        File.WriteAllText(Path.Combine(layout.HostDirectory("2027.1.0"), "extra.dll"), "x");
        BinaryDeployer.Deploy(kit, manifest, layout, replaceExisting: true);
        Assert.False(File.Exists(Path.Combine(layout.HostDirectory("2027.1.0"), "extra.dll")));

        File.WriteAllText(Path.Combine(kit, "app", "host", "Erp.Host.dll"), "kit modificat după verificare");
        File.WriteAllText(Path.Combine(layout.HostDirectory("2027.1.0"), "Erp.Host.dll"), "x");
        var ex = Assert.Throws<WindowsSetupException>(() => BinaryDeployer.Deploy(kit, manifest, layout, replaceExisting: false));
        Assert.Contains("app/host/Erp.Host.dll", ex.Message);
    }

    [Fact]
    public void Configuration_writer_creates_settings_secrets_and_web_config()
    {
        var kit = Path.Combine(root, "kit");
        Directory.CreateDirectory(Path.Combine(kit, "templates"));
        File.Copy(Path.Combine(TestPaths.RepositoryRoot(), "src", "Setup", "Erp.Setup", "Kit", "templates", "appsettings.Installation.json"),
            Path.Combine(kit, "templates", "appsettings.Installation.json"));
        var layout = Layout();
        var values = new InstallationSettingsValues("PROD", "2027.1.0", "https://erp.firma.ro", layout.Data.InstanceDirectory, "SQL01", "ERP_PROD",
            "Sql", "erp_prod_app", "Mandatory", false);

        Assert.Throws<WindowsSetupException>(() => ConfigurationWriter.Write(kit, layout, "2027.1.0", values, secrets: null));
        ConfigurationWriter.Write(kit, layout, "2027.1.0", values, new Dictionary<string, string> { ["Database:Password"] = "p" });
        ConfigurationWriter.Write(kit, layout, "2027.1.0", values, secrets: null);

        Assert.Contains("erp_prod_app", File.ReadAllText(layout.Data.AppSettings));
        Assert.Equal("p", InstallationSecrets.Read(layout.Data.Secrets, "PROD")["Database:Password"]);
        Assert.Contains(layout.Data.InstanceDirectory, File.ReadAllText(Path.Combine(layout.HostDirectory("2027.1.0"), "web.config")));
    }

    private static KitFile AddKitFile(string kit, string relative, string content)
    {
        var path = Path.Combine(kit, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return new KitFile(relative, KitVerifier.ComputeSha256(path), new FileInfo(path).Length);
    }

    private static List<FileSystemAccessRule> Rules(string path)
    {
        FileSystemSecurity security = Directory.Exists(path) ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        return security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
    }

    private static bool Has(string path, SecurityIdentifier identity, FileSystemRights rights) =>
        Rules(path).Any(r => r.IdentityReference == identity && r.AccessControlType == AccessControlType.Allow && (r.FileSystemRights & rights) == rights);
}
