using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Erp.Worker.Jobs;

/// <summary>
/// Jobul zilnic al utilizatorilor (02.01, 3.4): dezactiveaza externii cu accesul expirat si angajatii cu contractul incetat
/// (core.usp_User_DeactivateExpired: stare, audit, outbox) si le inchide sesiunile cu un SecurityStamp nou
/// (ids.usp_UserAccount_AdminReset), totul intr-o singura tranzactie. Ziua e cea UTC, ca in core.v_UserLoginStatus.
/// </summary>
public sealed class UserAccessExpirationJob(Func<SqlConnection> connect, TimeProvider clock, ILogger<UserAccessExpirationJob> log)
{
    public const string Id = "core-user-access-expiration";

    public async Task RunAsync(CancellationToken ct)
    {
        await using var connection = connect();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var correlationId = Guid.NewGuid();
        var deactivated = (await connection.QueryAsync<(int Id, Guid SubjectId, string Reason)>(new CommandDefinition(
            "core.usp_User_DeactivateExpired",
            new { Today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).ToDateTime(TimeOnly.MinValue), CorrelationId = correlationId },
            transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct))).ToList();

        foreach (var user in deactivated)
        {
            var hasAccount = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT 1 FROM ids.UserAccount WHERE SubjectId = @SubjectId", new { user.SubjectId }, transaction, cancellationToken: ct)) is not null;
            if (!hasAccount)
            {
                continue;
            }

            await connection.ExecuteAsync(new CommandDefinition("ids.usp_UserAccount_AdminReset", new
            {
                user.SubjectId,
                Action = new DbString { Value = "RevokeSessions", Length = 20, IsAnsi = true },
                PasswordHash = (string?)null,
                SecurityStamp = Stamp(),
                ConcurrencyStamp = Stamp(),
                ModifiedBy = 1,
            }, transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
        // Fara date personale in jurnal (NET-18): doar numarul si identificatorii interni.
        log.LogInformation("Utilizatori dezactivați automat: {Count} ({UserIds}); corelare {CorrelationId}.", deactivated.Count,
            string.Join(",", deactivated.Select(d => d.Id)), correlationId);
    }

    private static string Stamp() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
}
