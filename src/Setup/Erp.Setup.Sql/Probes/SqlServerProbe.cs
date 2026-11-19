using Dapper;
using Erp.Setup.Core.Checks;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql.Probes;

/// <summary>
/// Conexiunea operatorului la master (C-10): Windows authentication cu contul curent (R5), criptata (Encrypt = Mandatory),
/// timeout 15 s. Daca singura problema e certificatul serverului SQL, se reincearca fara verificarea lui, cu avertisment.
/// </summary>
public sealed class SqlServerConnector : ISqlServerConnector
{
    private readonly Action<SqlConnectionStringBuilder>? customize;

    public SqlServerConnector()
    {
    }

    /// <param name="customize">Doar testele de integrare: autentificarea SQL in containerul Linux.</param>
    internal SqlServerConnector(Action<SqlConnectionStringBuilder>? customize) => this.customize = customize;

    public static SqlConnectionStringBuilder BuildOperatorConnectionString(SqlTarget target, string database = "master") =>
        BuildOperatorConnectionString(target, database, customize: null);

    internal static SqlConnectionStringBuilder BuildOperatorConnectionString(SqlTarget target, string database, Action<SqlConnectionStringBuilder>? customize)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = target.Server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            ConnectTimeout = 15,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = target.TrustServerCertificate,
            ApplicationName = $"ERP-{target.Instance}-Setup",
            Pooling = false,
        };
        customize?.Invoke(builder);
        return builder;
    }

    public async Task<ISqlServerProbe> ConnectAsync(SqlTarget target, CancellationToken ct)
    {
        try
        {
            return new SqlServerProbe(await OpenAsync(target, ct).ConfigureAwait(false), target.TrustServerCertificate);
        }
        catch (SqlException ex) when (!target.TrustServerCertificate && IsCertificateFailure(ex))
        {
            try
            {
                return new SqlServerProbe(await OpenAsync(target with { TrustServerCertificate = true }, ct).ConfigureAwait(false), trusted: true);
            }
            catch (SqlException retry)
            {
                throw Failed(target, retry);
            }
        }
        catch (SqlException ex)
        {
            throw Failed(target, ex);
        }
    }

    /// <summary>Esecul vine doar din validarea certificatului TLS al serverului SQL (autoritate necunoscuta, nume diferit).</summary>
    public static bool IsCertificateFailure(SqlException ex) =>
        ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) || ex.InnerException?.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<SqlConnection> OpenAsync(SqlTarget target, CancellationToken ct)
    {
        var connection = new SqlConnection(BuildOperatorConnectionString(target, "master", customize).ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static SqlConnectFailedException Failed(SqlTarget target, SqlException ex) =>
        new($"Conexiunea la {target.Server} cu contul Windows curent a eșuat (eroare {ex.Number}): {ex.Message}", ex);
}

/// <summary>Interogarile verificarilor C-11…C-20, pe master, cu conexiunea operatorului.</summary>
public sealed class SqlServerProbe(SqlConnection connection, bool trusted) : ISqlServerProbe
{
    private const int Timeout = 30;

    public bool TrustedServerCertificate { get; } = trusted;

    public async Task<SqlServerInfo> GetServerInfoAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int)               AS MajorVersion,
                   CAST(SERVERPROPERTY('Edition') AS nvarchar(128))                 AS Edition,
                   CAST(SERVERPROPERTY('Collation') AS nvarchar(128))               AS Collation,
                   CAST(ISNULL(IS_SRVROLEMEMBER(N'sysadmin'), 0) AS bit)            AS IsSysadmin,
                   CAST(SERVERPROPERTY('MachineName') AS nvarchar(128))             AS MachineName,
                   SYSUTCDATETIME()                                                 AS UtcNow,
                   CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(260)) AS DefaultDataPath,
                   CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(260))  AS DefaultLogPath,
                   CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(260)) AS DefaultBackupPath;
            """;
        var row = await connection.QuerySingleAsync<ServerRow>(new CommandDefinition(sql, commandTimeout: Timeout, cancellationToken: ct)).ConfigureAwait(false);
        return new SqlServerInfo(row.MajorVersion, row.Edition, row.Collation, row.IsSysadmin, row.MachineName, row.UtcNow,
            Trim(row.DefaultDataPath), Trim(row.DefaultLogPath), Trim(row.DefaultBackupPath));
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

        // Numele bazei nu poate fi parametru intr-un nume in trei parti; e validat (08, 6.2) si citat (08, 5.6).
        var database = SqlText.QuoteName(databaseName);
        var detailsSql = $"""
            SELECT (SELECT COUNT(*) FROM {database}.sys.objects AS o WHERE o.is_ms_shipped = 0)
                 + (SELECT COUNT(*) FROM {database}.sys.schemas AS s WHERE s.schema_id > 4 AND s.schema_id < 16384) AS UserObjectCount,
                   (SELECT CAST(p.value AS nvarchar(100)) FROM {database}.sys.extended_properties AS p WHERE p.class = 0 AND p.name = N'ErpProduct')   AS ErpProduct,
                   (SELECT CAST(p.value AS nvarchar(100)) FROM {database}.sys.extended_properties AS p WHERE p.class = 0 AND p.name = N'ErpInstance')  AS ErpInstance,
                   (SELECT CAST(p.value AS nvarchar(100)) FROM {database}.sys.extended_properties AS p WHERE p.class = 0 AND p.name = N'ErpInstallId') AS ErpInstallId,
                   CAST(CASE WHEN OBJECT_ID(N'{database}.core.SchemaVersion', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS HasJournal;
            """;
        var details = await connection.QuerySingleAsync<DatabaseDetailsRow>(
            new CommandDefinition(detailsSql, commandTimeout: Timeout, cancellationToken: ct)).ConfigureAwait(false);
        string? lastMigration = null;
        if (details.HasJournal)
        {
            lastMigration = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                $"SELECT TOP (1) v.ScriptName FROM {database}.core.SchemaVersion AS v ORDER BY v.Id DESC;", commandTimeout: Timeout, cancellationToken: ct))
                .ConfigureAwait(false);
        }

        return new DatabaseInfo(row.Collation ?? string.Empty, details.UserObjectCount, details.ErpProduct, details.ErpInstance, details.ErpInstallId,
            lastMigration, row.SizeBytes ?? 0);
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken ct) =>
        await connection.ExecuteAsync(new CommandDefinition("EXEC master.dbo.xp_create_subdir @Path;", new { Path = path },
            commandTimeout: Timeout, cancellationToken: ct)).ConfigureAwait(false);

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    private static string? Trim(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.TrimEnd('\\');

    private sealed record ServerRow(int MajorVersion, string Edition, string Collation, bool IsSysadmin, string MachineName, DateTime UtcNow,
        string? DefaultDataPath, string? DefaultLogPath, string? DefaultBackupPath);

    private sealed record DatabaseRow(string? Collation, long? SizeBytes);

    private sealed record DatabaseDetailsRow(int UserObjectCount, string? ErpProduct, string? ErpInstance, string? ErpInstallId, bool HasJournal);
}
