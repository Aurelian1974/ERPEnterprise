using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.LicenseGen.Core;
using Erp.Licensing;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Kit;

namespace Erp.Setup.Tests.Checks;

/// <summary>Servicii simulate pentru verificarile C-01…C-23; implicit toate cerintele sunt indeplinite.</summary>
internal sealed class FakeWindowsProbe : IWindowsProbe
{
    public bool Administrator { get; set; } = true;

    public OsInfo Os { get; set; } = new(20348, "ServerNT", true);

    public int LogicalProcessorCount { get; set; } = 8;

    public long TotalPhysicalMemoryBytes { get; set; } = 32L * 1024 * 1024 * 1024;

    public string MachineName { get; set; } = "SRV-ERP";

    public Dictionary<string, long?> FreeSpace { get; } = new(StringComparer.OrdinalIgnoreCase);

    public long? DefaultFreeSpace { get; set; } = 100L * 1024 * 1024 * 1024;

    public IReadOnlySet<string>? Features { get; set; } = RequirementsChecker.IisFeatures.ToHashSet();

    public List<Version> AspNetCoreVersions { get; } = [new(10, 0, 3)];

    public bool AspNetCoreModule { get; set; } = true;

    public CertificateInfo? Certificate { get; set; } = new(true, DateTimeOffset.UtcNow.AddYears(1), ["erp.grupulexemplu.local"], true);

    public List<IisBinding> Bindings { get; } = [];

    public HashSet<string> Sites { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Services { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Domain { get; set; } = "GRUPULEXEMPLU";

    public HashSet<string> ServiceAccounts { get; } = new(StringComparer.OrdinalIgnoreCase) { @"GRUPULEXEMPLU\erp-prod$" };

    public bool IsAdministrator() => Administrator;

    public OsInfo GetOperatingSystem() => Os;

    public long? GetFreeSpaceBytes(string path) => FreeSpace.TryGetValue(path, out var value) ? value : DefaultFreeSpace;

    public IReadOnlySet<string>? GetEnabledWindowsFeatures() => Features;

    public IReadOnlyList<Version> GetAspNetCoreRuntimeVersions() => AspNetCoreVersions;

    public bool IsAspNetCoreModuleInstalled() => AspNetCoreModule;

    public CertificateInfo? FindMachineCertificate(string thumbprint) => Certificate;

    public IReadOnlyList<IisBinding> GetIisBindings() => Bindings;

    public bool IisSiteExists(string siteName) => Sites.Contains(siteName);

    public bool ServiceExists(string serviceName) => Services.Contains(serviceName);

    public string? GetComputerDomain() => Domain;

    public bool IsServiceAccountUsable(string account) => ServiceAccounts.Contains(account);
}

internal sealed class FakeSqlServer : ISqlServerConnector, ISqlServerProbe
{
    public string? ConnectError { get; set; }

    /// <summary>Conexiunea reuseste doar fara verificarea certificatului serverului (C-10, avertisment).</summary>
    public bool TrustedServerCertificate { get; set; }

    public bool Disposed { get; private set; }

    public SqlServerInfo Info { get; set; } = new(16, "Standard Edition (64-bit)", "SQL_Latin1_General_CP1_CI_AS", true, "SRV-ERP",
        DateTime.UtcNow, @"D:\SQLData\", @"L:\SQLLog\", @"B:\SQLBackup");

    public string? AgentStatus { get; set; } = "Running";

    public DatabaseInfo? Database { get; set; }

    public HashSet<string> FailingDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> CreatedDirectories { get; } = [];

    public Task<ISqlServerProbe> ConnectAsync(SqlTarget target, CancellationToken ct) =>
        ConnectError is null
            ? Task.FromResult<ISqlServerProbe>(this)
            : throw new SqlConnectFailedException(ConnectError);

    public Task<SqlServerInfo> GetServerInfoAsync(CancellationToken ct) => Task.FromResult(Info);

    public Task<string?> GetAgentStatusAsync(CancellationToken ct) => Task.FromResult(AgentStatus);

    public Task<DatabaseInfo?> GetDatabaseAsync(string databaseName, CancellationToken ct) => Task.FromResult(Database);

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        if (FailingDirectories.Contains(path))
        {
            throw new InvalidOperationException($"Acces refuzat: {path}");
        }

        CreatedDirectories.Add(path);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeNetwork : INetworkProbe
{
    public HashSet<string> Unreachable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> CanReachAsync(HttpMethod method, Uri uri, TimeSpan timeout, CancellationToken ct) =>
        Task.FromResult(!Unreachable.Contains(uri.Host));
}

/// <summary>Licente de test in formatul binar (08, 3.4), scrise cu o cheie generata in test (KeyId 1).</summary>
internal sealed class TestLicenses : IDisposable
{
    public const string KeyId = "1";

    private readonly LicenseSigningKey key = LicenseSigningKey.Generate(1);

    public LicenseReader Reader => new(new LicenseKeyRing([key.ToPublicKey()]), TimeProvider.System);

    public static License Payload(string? validUntil = "2099-12-31", string[]? modules = null) => new()
    {
        LicenseId = "LIC-2026-000123",
        Product = License.ProductCode,
        CustomerName = "Grupul Exemplu",
        FiscalCodes = "10000008",
        Modules = modules ?? ["Core", "Finance", "Sales"],
        Limits = new LicenseLimits(100, 300, 10),
        IssuedAt = new DateOnly(2016, 11, 14),
        ValidUntil = validUntil is null ? null : DateOnly.Parse(validUntil, System.Globalization.CultureInfo.InvariantCulture),
    };

    public byte[] Create(License payload) => LicenseWriter.Write(payload, key);

    public void Dispose() => key.Dispose();
}

internal static class CheckFixtures
{
    public static KitManifest Manifest => new()
    {
        Product = "ERP",
        Version = "2027.1.0",
        ReleaseDate = "2027-01-20",
        MinUpgradeFrom = "2026.2.0",
        MinSqlServerMajorVersion = 16,
        SqlCompatibilityLevel = 160,
        DatabaseCollation = RequirementsChecker.RequiredCollation,
        HostingBundleVersion = "10.0.3",
        Files = [],
    };

    public static InstallRequest Request(string licensePath) => new("PROD", @"C:\Program Files\Erp",
        new SqlRequest(@"SRV-ERP", "ERP_PROD", null, null, AppIdentity.VirtualAccounts, null),
        new SiteRequest("erp.grupulexemplu.local", 443, true, CertificateKind.Internal, null),
        new BackupRequest(null, new TimeOnly(1, 0), 15, 14, DayOfWeek.Saturday, new TimeOnly(2, 0)),
        licensePath, new HashSet<string>())
    {
        DataRoot = @"C:\ProgramData\Erp",
    };

    public static CheckTarget Target(string licensePath, CheckMode mode = CheckMode.Check) => CheckTarget.From(Request(licensePath), mode);

    public static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"erp-lic-{Guid.NewGuid():N}.lic");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public static string WriteTemp(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"erp-lic-{Guid.NewGuid():N}.lic");
        File.WriteAllBytes(path, content);
        return path;
    }
}
