namespace Erp.Setup.Core.Checks;

/// <summary>Faptele citite de pe serverul de aplicatie; interpretarea lor e in RequirementsChecker.</summary>
public interface IWindowsProbe
{
    bool IsAdministrator();

    OsInfo GetOperatingSystem();

    int LogicalProcessorCount { get; }

    long TotalPhysicalMemoryBytes { get; }

    /// <summary>Numele scurt al calculatorului (NetBIOS).</summary>
    string MachineName { get; }

    /// <summary>Spatiul liber pe volumul caii (calea poate sa nu existe inca); null daca volumul nu exista.</summary>
    long? GetFreeSpaceBytes(string path);

    /// <summary>Functiile Windows activate, cu numele DISM (de exemplu „IIS-WebServerRole”); null daca nu se pot interoga.</summary>
    IReadOnlySet<string>? GetEnabledWindowsFeatures();

    IReadOnlyList<Version> GetAspNetCoreRuntimeVersions();

    bool IsAspNetCoreModuleInstalled();

    CertificateInfo? FindMachineCertificate(string thumbprint);

    IReadOnlyList<IisBinding> GetIisBindings();

    bool IisSiteExists(string siteName);

    bool ServiceExists(string serviceName);

    /// <summary>Domeniul Active Directory al calculatorului; null daca e in grup de lucru.</summary>
    string? GetComputerDomain();

    /// <summary>Contul gMSA exista si poate fi folosit pe acest calculator (NetIsServiceAccount, 08, C-15).</summary>
    bool IsServiceAccountUsable(string account);
}

public sealed record OsInfo(int Build, string ProductType, bool Is64Bit)
{
    /// <summary>ProductType „WinNT” = statie de lucru (Windows 10/11); altfel Windows Server.</summary>
    public bool IsServer => !string.Equals(ProductType, "WinNT", StringComparison.OrdinalIgnoreCase);
}

public sealed record CertificateInfo(
    bool HasPrivateKey,
    DateTimeOffset NotAfter,
    IReadOnlyList<string> DnsNames,
    bool HasServerAuthenticationEku);

public sealed record IisBinding(string SiteName, string Protocol, int Port, string HostName);

/// <summary>Faptele citite de pe SQL Server cu conexiunea operatorului (Windows authentication).</summary>
public interface ISqlServerProbe : IAsyncDisposable
{
    /// <summary>Conexiunea a reusit doar acceptand certificatul serverului fara verificare (C-10, avertisment).</summary>
    bool TrustedServerCertificate { get; }

    Task<SqlServerInfo> GetServerInfoAsync(CancellationToken ct);

    /// <summary>Starea serviciului SQL Server Agent (de exemplu „Running”); null daca nu se poate citi.</summary>
    Task<string?> GetAgentStatusAsync(CancellationToken ct);

    /// <summary>null daca baza nu exista.</summary>
    Task<DatabaseInfo?> GetDatabaseAsync(string databaseName, CancellationToken ct);

    /// <summary>Creeaza folderul cu contul serviciului SQL Server (xp_create_subdir, 08, C-16, C-20).</summary>
    Task CreateDirectoryAsync(string path, CancellationToken ct);
}

public interface ISqlServerConnector
{
    /// <summary>
    /// Deschide conexiunea la master cu contul Windows curent (C-10): intai cu certificatul serverului verificat, apoi, daca doar
    /// verificarea certificatului esueaza, fara ea. Arunca <see cref="SqlConnectFailedException"/> la esec.
    /// </summary>
    Task<ISqlServerProbe> ConnectAsync(SqlTarget target, CancellationToken ct);
}

public sealed class SqlConnectFailedException(string message, Exception? inner = null) : Exception(message, inner);

/// <param name="MachineName">SERVERPROPERTY('MachineName'): calculatorul pe care ruleaza SQL Server (C-15).</param>
public sealed record SqlServerInfo(
    int MajorVersion,
    string Edition,
    string Collation,
    bool IsSysadmin,
    string MachineName,
    DateTime UtcNow,
    string? DefaultDataPath,
    string? DefaultLogPath,
    string? DefaultBackupPath)
{
    public bool IsExpress => Edition.StartsWith("Express", StringComparison.OrdinalIgnoreCase);

    public bool IsDeveloper => Edition.StartsWith("Developer", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Starea unei baze existente (08, C-14).</summary>
/// <param name="UserObjectCount">Obiecte cu is_ms_shipped = 0 plus scheme proprii; 0 = baza e goala.</param>
/// <param name="LastMigration">Ultimul script din core.SchemaVersion; null daca jurnalul lipseste.</param>
public sealed record DatabaseInfo(string Collation, int UserObjectCount, string? ErpProduct, string? ErpInstance, string? ErpInstallId,
    string? LastMigration, long SizeBytes)
{
    public bool IsEmpty => UserObjectCount == 0;

    public bool IsErp => string.Equals(ErpProduct, "ERP", StringComparison.Ordinal);
}

public interface INetworkProbe
{
    /// <summary>true daca adresa raspunde (orice cod HTTP) in timpul dat, prin proxy-ul sistemului.</summary>
    Task<bool> CanReachAsync(HttpMethod method, Uri uri, TimeSpan timeout, CancellationToken ct);
}
