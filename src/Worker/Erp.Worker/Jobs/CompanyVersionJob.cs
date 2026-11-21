using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Erp.Worker.Jobs;

/// <summary>
/// Jobul zilnic al datelor firmelor (02.01, 3.6 punctul 2): aplica versiunile programate a caror data a sosit (de exemplu „TVA la
/// încasare de la 01.03”), prin core.usp_CompanyVersion_Apply. Ziua e cea UTC, ca la salvare.
/// </summary>
public sealed class CompanyVersionJob(Func<SqlConnection> connect, TimeProvider clock, ILogger<CompanyVersionJob> log)
{
    public const string Id = "core-company-versions";

    public async Task RunAsync(CancellationToken ct)
    {
        await using var connection = connect();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var changed = (await connection.QueryAsync<int>(new CommandDefinition("core.usp_CompanyVersion_Apply",
            new { Today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).ToDateTime(TimeOnly.MinValue) },
            transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct))).ToList();
        await transaction.CommitAsync(ct);
        log.LogInformation("Date de firmă aplicate de la data programată: {Count} ({CompanyIds}).", changed.Count, string.Join(",", changed));
    }
}
