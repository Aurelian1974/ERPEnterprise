namespace Erp.Setup.Core.Checks;

/// <summary>Faptele citite de pe serverul de aplicatie; interpretarea lor e in RequirementsChecker.</summary>
public interface IWindowsProbe
{
    bool IsAdministrator();

    OsInfo GetOperatingSystem();

    int LogicalProcessorCount { get; }

    long TotalPhysicalMemoryBytes { get; }

    /// <summary>Spatiul liber pe volumul caii (calea poate sa nu existe inca); null daca volumul nu exista.</summary>
    long? GetFreeSpaceBytes(string path);

    /// <summary>Numele functiilor IIS instalate (de exemplu „Web-Server”); null daca nu se pot interoga.</summary>
    IReadOnlySet<string>? GetInstalledWindowsFeatures();

    IReadOnlyList<Version> GetAspNetCoreRuntimeVersions();

    bool IsAspNetCoreModuleInstalled();

    CertificateInfo? FindMachineCertificate(string thumbprint);

    IReadOnlyList<IisBinding> GetIisBindings();

    /// <summary>Amprenta certificatului legat in http.sys pe hostName:port (SNI); null daca nu exista.</summary>
    string? GetSniBindingThumbprint(string hostName, int port);

    bool IsTls12ClientDisabled();

    bool IisSiteExists(string siteName);

    bool ServiceExists(string serviceName);
}

public sealed record OsInfo(int Build, string ProductType, bool Is64Bit);

public sealed record CertificateInfo(
    bool HasPrivateKey,
    DateTimeOffset NotAfter,
    IReadOnlyList<string> DnsNames,
    bool HasServerAuthenticationEku);

public sealed record IisBinding(string SiteName, string Protocol, int Port, string HostName);

/// <summary>Faptele citite de pe SQL Server cu conexiunea operatorului.</summary>
public interface ISqlServerProbe : IAsyncDisposable
{
    Task<SqlServerInfo> GetServerInfoAsync(CancellationToken ct);

    /// <summary>Starea serviciului SQL Server Agent (de exemplu „Running”); null daca nu se poate citi.</summary>
    Task<string?> GetAgentStatusAsync(CancellationToken ct);

    /// <summary>null daca baza nu exista.</summary>
    Task<DatabaseInfo?> GetDatabaseAsync(string databaseName, CancellationToken ct);

    Task<SqlDirectoryState> GetDirectoryStateAsync(string path, CancellationToken ct);

    /// <summary>Creeaza folderul cu contul serviciului SQL Server (xp_create_subdir); numai la install/upgrade.</summary>
    Task CreateDirectoryAsync(string path, CancellationToken ct);

    /// <summary>Spatiul liber pe volumul caii, vazut de SQL Server; null daca nu se poate determina.</summary>
    Task<long?> GetFreeSpaceBytesAsync(string path, CancellationToken ct);
}

public interface ISqlServerConnector
{
    /// <summary>Deschide conexiunea la master (C-09); arunca <see cref="SqlConnectFailedException"/> la esec.</summary>
    Task<ISqlServerProbe> ConnectAsync(SqlTarget target, string? operatorPassword, CancellationToken ct);
}

public sealed class SqlConnectFailedException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record SqlServerInfo(
    int MajorVersion,
    int EngineEdition,
    string Edition,
    string Collation,
    bool IsSysadmin,
    bool IsDbCreator,
    bool IsSecurityAdmin,
    DateTime UtcNow,
    string? DefaultDataPath,
    string? DefaultLogPath);

public sealed record DatabaseInfo(string Collation, int UserTableCount, string? ErpInstallId, long SizeBytes);

public enum SqlDirectoryState
{
    Exists,
    WillBeCreated,
    Unreachable,
}

public interface INetworkProbe
{
    /// <summary>true daca adresa raspunde (orice cod HTTP) in timpul dat, prin proxy-ul sistemului.</summary>
    Task<bool> CanReachAsync(HttpMethod method, Uri uri, TimeSpan timeout, CancellationToken ct);
}
