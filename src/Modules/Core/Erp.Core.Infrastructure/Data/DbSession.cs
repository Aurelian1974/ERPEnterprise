using System.Data;
using System.Data.Common;
using Erp.Core.Application.Abstractions;
using Erp.Core.Contracts;
using Microsoft.Data.SqlClient;

namespace Erp.Core.Infrastructure.Data;

/// <summary>
/// O singura conexiune SQL per cerere (NET-07), deschisa la prima folosire. TransactionBehavior porneste tranzactia
/// ReadCommitted (baza are READ_COMMITTED_SNAPSHOT ON, SP-14); toate store-urile folosesc Connection si Transaction de aici.
/// </summary>
public sealed class DbSession(string connectionString) : IDbSession, IUnitOfWork, IAsyncDisposable
{
    private SqlConnection? connection;
    private SqlTransaction? transaction;

    public DbConnection Connection => connection ??= new SqlConnection(connectionString);

    public DbTransaction? Transaction => transaction;

    public async Task BeginAsync(CancellationToken ct)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException("Tranzacția cererii este deja deschisă.");
        }

        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(ct).ConfigureAwait(false);
        }

        transaction = (SqlTransaction)await Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        var current = transaction ?? throw new InvalidOperationException("Nu există o tranzacție deschisă.");
        transaction = null;
        await current.CommitAsync(ct).ConfigureAwait(false);
        await current.DisposeAsync().ConfigureAwait(false);
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        var current = transaction;
        if (current is null)
        {
            return;
        }

        transaction = null;
        // Dupa un deadlock SQL Server a anulat deja tranzactia; Rollback pe ea ar arunca.
        if (current.Connection is not null)
        {
            await current.RollbackAsync(ct).ConfigureAwait(false);
        }

        await current.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Conexiunea deschisa, pentru citiri in afara tranzactiei (interogari).</summary>
    public async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(ct).ConfigureAwait(false);
        }

        return Connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>SP-17: deadlock (1205) si expirarea blocarii (1222) se reincearca.</summary>
public sealed class SqlServerTransientFailures : ITransientFailureDetector
{
    public bool IsTransient(Exception exception) => exception is SqlException { Number: 1205 or 1222 };
}
