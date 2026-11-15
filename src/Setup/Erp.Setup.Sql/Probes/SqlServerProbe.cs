using Dapper;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql.Probes;

/// <summary>Conexiunea operatorului la master (C-09): timeout 15 s, criptarea din configurare.</summary>
public sealed class SqlServerConnector : ISqlServerConnector
{
    public static SqlConnectionStringBuilder BuildOperatorConnectionString(SqlTarget target, string? operatorPassword)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = target.Server,
            InitialCatalog = "master",
            ConnectTimeout = 15,
            Encrypt = target.Encrypt switch
            {
                SqlEncrypt.Strict => SqlConnectionEncryptOption.Strict,
                SqlEncrypt.Optional => SqlConnectionEncryptOption.Optional,
                _ => SqlConnectionEncryptOption.Mandatory,
            },
            TrustServerCertificate = target.TrustServerCertificate,
            ApplicationName = $"ERP-{target.Instance}-Setup",
            Pooling = false,
        };

        if (target.OperatorAuthentication == SqlAuthentication.Sql)
        {
            builder.UserID = target.OperatorSqlLogin;
            builder.Password = operatorPassword;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder;
    }

    public async Task<ISqlServerProbe> ConnectAsync(SqlTarget target, string? operatorPassword, CancellationToken ct)
    {
        var connection = new SqlConnection(BuildOperatorConnectionString(target, operatorPassword).ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return new SqlServerProbe(connection);
        }
        catch (SqlException ex)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            // Mesajul SqlClient nu contine parola; contine serverul si, la autentificare SQL, numele loginului.
            throw new SqlConnectFailedException($"Conexiunea la {target.Server} a eșuat (eroare {ex.Number}): {ex.Message}", ex);
        }
    }
}

/// <summary>Interogarile pentru C-10…C-19, pe master, cu conexiunea operatorului.</summary>
public sealed class SqlServerProbe(SqlConnection connection) : ISqlServerProbe
{
    private const int Timeout = 30;

    public async Task<SqlServerInfo> GetServerInfoAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int)           AS MajorVersion,
                   CAST(SERVERPROPERTY('EngineEdition') AS int)                 AS EngineEdition,
                   CAST(SERVERPROPERTY('Edition') AS nvarchar(128))             AS Edition,
                   CAST(SERVERPROPERTY('Collation') AS nvarchar(128))           AS Collation,
                   CAST(ISNULL(IS_SRVROLEMEMBER(N'sysadmin'), 0) AS bit)        AS IsSysadmin,
                   CAST(ISNULL(IS_SRVROLEMEMBER(N'dbcreator'), 0) AS bit)       AS IsDbCreator,
                   CAST(ISNULL(IS_SRVROLEMEMBER(N'securityadmin'), 0) AS bit)   AS IsSecurityAdmin,
                   SYSUTCDATETIME()                                             AS UtcNow,
                   CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(260)) AS DefaultDataPath,
                   CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(260))  AS DefaultLogPath;
            """;
        var row = await connection.QuerySingleAsync<ServerRow>(new CommandDefinition(sql, commandTimeout: Timeout, cancellationToken: ct)).ConfigureAwait(false);
        return new SqlServerInfo(row.MajorVersion, row.EngineEdition, row.Edition, row.Collation, row.IsSysadmin, row.IsDbCreator,
            row.IsSecurityAdmin, row.UtcNow, row.DefaultDataPath, row.DefaultLogPath);
    }

    public async Task<string?> GetAgentStatusAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) s.status_desc
            FROM sys.dm_server_services AS s
            WHERE s.servicename LIKE N'SQL Server Agent%';
            """;
        try
        {
            return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(sql, commandTimeout: Timeout, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (SqlException)
        {
            // Fara VIEW SERVER STATE starea nu se poate citi; C-13 raporteaza agentul ca nepornit.
            return null;
        }
    }

    public async Task<DatabaseInfo?> GetDatabaseAsync(string databaseName, CancellationToken ct)
    {
        const string sql = """
            SELECT CAST(DATABASEPROPERTYEX(d.name, 'Collation') AS nvarchar(128)) AS Collation,
                   (SELECT SUM(CAST(f.size AS bigint)) * 8192 FROM sys.master_files AS f WHERE f.database_id = d.database_id) AS SizeBytes
            FROM sys.databases AS d
            WHERE d.name = @DatabaseName;
            """;
        var row = await connection.QuerySingleOrDefaultAsync<DatabaseRow>(
            new CommandDefinition(sql, new { DatabaseName = databaseName }, commandTimeout: Timeout, cancellationToken: ct)).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        // Numele bazei nu poate fi parametru intr-un nume in trei parti; e validat (6.2) si citat (8.2).
        var database = SqlText.QuoteName(databaseName);
        var detailsSql = $"""
            SELECT (SELECT COUNT(*) FROM {database}.sys.tables AS t WHERE t.is_ms_shipped = 0) AS UserTableCount,
                   (SELECT CAST(p.value AS nvarchar(100)) FROM {database}.sys.extended_properties AS p
                    WHERE p.class = 0 AND p.name = N'ErpInstallId') AS ErpInstallId;
            """;
        var details = await connection.QuerySingleAsync<DatabaseDetailsRow>(
            new CommandDefinition(detailsSql, commandTimeout: Timeout, cancellationToken: ct)).ConfigureAwait(false);
        return new DatabaseInfo(row.Collation ?? string.Empty, details.UserTableCount, details.ErpInstallId, row.SizeBytes ?? 0);
    }

    public async Task<SqlDirectoryState> GetDirectoryStateAsync(string path, CancellationToken ct)
    {
        const string sql = """
            SELECT CAST(f.file_is_a_directory AS bit) AS IsDirectory
            FROM sys.dm_os_file_exists(@Path) AS f;
            """;
        var current = path;
        var first = true;
        while (!string.IsNullOrEmpty(current))
        {
            var isDirectory = await connection.QuerySingleOrDefaultAsync<bool?>(
                new CommandDefinition(sql, new { Path = current }, commandTimeout: Timeout, cancellationToken: ct)).ConfigureAwait(false);
            if (isDirectory == true)
            {
                return first ? SqlDirectoryState.Exists : SqlDirectoryState.WillBeCreated;
            }

            first = false;
            current = ParentOf(current);
        }

        return SqlDirectoryState.Unreachable;
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition("EXEC master.dbo.xp_create_subdir @Path;", new { Path = path },
            commandTimeout: Timeout, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<long?> GetFreeSpaceBytesAsync(string path, CancellationToken ct)
    {
        const string sql = """
            SELECT d.fixed_drive_path AS DrivePath, CAST(d.free_space_in_bytes AS bigint) AS FreeBytes
            FROM sys.dm_os_enumerate_fixed_drives AS d;
            """;
        IEnumerable<DriveRow> drives;
        try
        {
            drives = await connection.QueryAsync<DriveRow>(new CommandDefinition(sql, commandTimeout: Timeout, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (SqlException)
        {
            return null;
        }

        return drives
            .Where(d => !string.IsNullOrEmpty(d.DrivePath) && path.StartsWith(d.DrivePath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.DrivePath.Length)
            .Select(d => (long?)d.FreeBytes)
            .FirstOrDefault();
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    internal static string? ParentOf(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 0 || (trimmed.Length == 2 && trimmed[1] == ':'))
        {
            return null;
        }

        var index = trimmed.LastIndexOfAny(['\\', '/']);
        if (index < 0)
        {
            return null;
        }

        var parent = trimmed[..index];
        if (parent.Length == 0)
        {
            return "/";
        }

        if (parent.Length == 2 && parent[1] == ':')
        {
            return parent + "\\";
        }

        // \\server\share este radacina unei cai UNC.
        return parent.StartsWith(@"\\", StringComparison.Ordinal) && parent.IndexOf('\\', 2) < 0 ? null : parent;
    }

    private sealed record ServerRow(int MajorVersion, int EngineEdition, string Edition, string Collation, bool IsSysadmin,
        bool IsDbCreator, bool IsSecurityAdmin, DateTime UtcNow, string? DefaultDataPath, string? DefaultLogPath);

    private sealed record DatabaseRow(string? Collation, long? SizeBytes);

    private sealed record DatabaseDetailsRow(int UserTableCount, string? ErpInstallId);

    private sealed record DriveRow(string DrivePath, long FreeBytes);
}
