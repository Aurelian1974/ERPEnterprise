using System.Diagnostics;
using Erp.Setup.Core;
using Erp.Setup.Core.Checks;
using Erp.Setup.Sql.Probes;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.Setup.Sql;

/// <summary>Eroare SQL la instalare, cu codul de iesire din 08, 6.3 (implicit 30). Mesajul nu contine secrete.</summary>
public sealed class SqlSetupException(string message, int exitCode = ExitCodes.SqlInstallError, Exception? inner = null)
    : Exception(message, inner)
{
    public int ExitCode { get; } = exitCode;
}

/// <summary>
/// Conexiunile operatorului (Windows authentication, contul curent, R5) si executia comenzilor installer-ului. Migratiile ruleaza
/// tot cu aceasta conexiune: nu exista un login SQL separat pentru migratii (08, 5.8, SP-19).
/// </summary>
public sealed class SqlOperator
{
    public const int CommandTimeoutSeconds = 600;

    private readonly Action<SqlConnectionStringBuilder>? customize;

    public SqlOperator(SqlTarget target, ILogger log)
        : this(target, log, customize: null)
    {
    }

    /// <param name="customize">Doar testele de integrare: autentificarea SQL in containerul Linux, unde Windows authentication lipseste.</param>
    internal SqlOperator(SqlTarget target, ILogger log, Action<SqlConnectionStringBuilder>? customize)
    {
        Target = target;
        Log = log;
        this.customize = customize;
    }

    public SqlTarget Target { get; }

    public ILogger Log { get; }

    /// <summary>Sirul de conexiune al operatorului pentru o baza (migratiile DbUp, procedurile installer-ului).</summary>
    public string ConnectionString(string database) =>
        SqlServerConnector.BuildOperatorConnectionString(Target, database, customize).ConnectionString;

    public async Task<SqlConnection> OpenAsync(string database, CancellationToken ct)
    {
        var connection = new SqlConnection(ConnectionString(database));
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

    public async Task ExecuteAsync(SqlConnection connection, string description, string sql, CancellationToken ct, params SqlParameter[] parameters)
    {
        var watch = Stopwatch.StartNew();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddRange(parameters);
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            Log.Error("{Description} a eșuat după {ElapsedMs} ms: eroarea SQL {Number}: {Message}", description, watch.ElapsedMilliseconds, ex.Number, ex.Message);
            throw new SqlSetupException($"{description}: eroarea SQL {ex.Number}: {ex.Message}", inner: ex);
        }

        Log.Information("{Description}: OK ({ElapsedMs} ms)", description, watch.ElapsedMilliseconds);
    }

    public static async Task<T?> ScalarAsync<T>(SqlConnection connection, string sql, CancellationToken ct, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? default : (T)value;
    }
}
