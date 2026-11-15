using System.Diagnostics;
using Erp.Setup.Core;
using Erp.Setup.Core.Checks;
using Erp.Setup.Sql.Probes;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.Setup.Sql;

/// <summary>Eroare SQL la instalare, cu codul de iesire din 6.4 (implicit 30). Mesajul nu contine secrete.</summary>
public sealed class SqlSetupException(string message, int exitCode = ExitCodes.SqlInstallError, Exception? inner = null)
    : Exception(message, inner)
{
    public int ExitCode { get; } = exitCode;
}

/// <summary>
/// Conexiunile operatorului si executia comenzilor installer-ului. Jurnalul primeste doar descrierea comenzii si durata,
/// niciodata textul SQL (6.6, R6): textul comenzilor de login contine parole.
/// </summary>
public sealed class SqlOperator(SqlTarget target, string? operatorPassword, ILogger log)
{
    public const int CommandTimeoutSeconds = 600;

    public SqlTarget Target { get; } = target;

    public ILogger Log { get; } = log;

    public async Task<SqlConnection> OpenAsync(string database, CancellationToken ct)
    {
        var builder = SqlServerConnector.BuildOperatorConnectionString(Target, operatorPassword);
        builder.InitialCatalog = database;
        var connection = new SqlConnection(builder.ConnectionString);
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

    /// <summary>Conexiune cu alt login SQL (de exemplu loginul de migrare), cu aceleasi setari de server si criptare.</summary>
    public SqlConnectionStringBuilder ConnectionStringFor(string database, string login, string password)
    {
        var builder = SqlServerConnector.BuildOperatorConnectionString(Target with { OperatorAuthentication = Core.Answers.SqlAuthentication.Sql, OperatorSqlLogin = login }, password);
        builder.InitialCatalog = database;
        return builder;
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
