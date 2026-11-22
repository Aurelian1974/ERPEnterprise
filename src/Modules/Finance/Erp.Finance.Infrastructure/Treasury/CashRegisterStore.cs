using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Treasury;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Erp.Finance.Infrastructure.Treasury;

/// <summary>Registrul de casa prin fin.usp_CashRegisterDay_Close/_Reopen si fin.usp_CashRegister_Get (NET-06).</summary>
public sealed class CashRegisterStore(IDbSession session) : ICashRegisterStore
{
    public Task<CashDayClosing> CloseAsync(int companyId, int treasuryAccountId, DateOnly day, int userId, CancellationToken ct) =>
        CloseAsync(session.Connection, session.Transaction, companyId, treasuryAccountId, day, userId, ct);

    internal static async Task<CashDayClosing> CloseAsync(System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction? transaction,
        int companyId, int treasuryAccountId, DateOnly day, int userId, CancellationToken ct)
    {
        var row = await connection.QuerySingleAsync<ClosingRow>(new CommandDefinition("fin.usp_CashRegisterDay_Close", new
        {
            TreasuryAccountId = treasuryAccountId, CompanyId = companyId, Day = day.ToDateTime(TimeOnly.MinValue), UserId = userId,
        }, transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        return new CashDayClosing(row.CashRegisterDayId, DateOnly.FromDateTime(row.Day), row.OpeningBalance, row.TotalIn, row.TotalOut, row.ClosingBalance,
            row.FirstPageNo, row.LastPageNo, row.MaxClosingBalance, row.LegalLimit, row.CurrencyCode.Trim());
    }

    public async Task<IReadOnlyList<DateOnly>> ReopenAsync(int companyId, int treasuryAccountId, DateOnly day, string reason, int userId,
        CancellationToken ct) =>
        (await session.Connection.QueryAsync<DateTime>(new CommandDefinition("fin.usp_CashRegisterDay_Reopen", new
        {
            TreasuryAccountId = treasuryAccountId, CompanyId = companyId, Day = day.ToDateTime(TimeOnly.MinValue), Reason = reason, UserId = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false))
        .Select(DateOnly.FromDateTime).ToList();

    public async Task<(IReadOnlyList<CashRegisterDayRow> Days, IReadOnlyList<CashRegisterDocumentRow> Documents)?> GetAsync(int companyId,
        int treasuryAccountId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("fin.usp_CashRegister_Get", new
        {
            CompanyId = companyId, TreasuryAccountId = treasuryAccountId, From = from.ToDateTime(TimeOnly.MinValue), To = to.ToDateTime(TimeOnly.MinValue),
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        if (grid.IsConsumed)
        {
            return null;
        }

        var days = (await grid.ReadAsync<DayRow>().ConfigureAwait(false)).Select(d => new CashRegisterDayRow(DateOnly.FromDateTime(d.Day), d.OpeningBalance,
            d.TotalIn, d.TotalOut, d.ClosingBalance, d.Status == 2, d.FirstPageNo, d.LastPageNo, d.ClosedAt, d.ClosedByName, d.ReopenedAt, d.ReopenedByName,
            d.ReopenReason, d.DraftCount)).ToList();
        var documents = (await grid.ReadAsync<DocumentRow>().ConfigureAwait(false)).Select(d => new CashRegisterDocumentRow(d.Id,
            DateOnly.FromDateTime(d.DocumentDate), (CashDocumentKind)d.Kind, d.Number, d.PersonName, d.Description, d.TotalAmount, (CashDocumentStatus)d.Status,
            d.ReversalOfId is not null, d.ValidatedAt)).ToList();
        return (days, documents);
    }

    private sealed record ClosingRow(int CashRegisterDayId, DateTime Day, decimal OpeningBalance, decimal TotalIn, decimal TotalOut, decimal ClosingBalance,
        int? FirstPageNo, int? LastPageNo, decimal? MaxClosingBalance, decimal? LegalLimit, string CurrencyCode);

    private sealed record DayRow(DateTime Day, decimal OpeningBalance, decimal TotalIn, decimal TotalOut, decimal ClosingBalance, byte Status, int? FirstPageNo,
        int? LastPageNo, DateTime? ClosedAt, string? ClosedByName, DateTime? ReopenedAt, string? ReopenedByName, string? ReopenReason, int DraftCount);

    private sealed record DocumentRow(int Id, DateTime DocumentDate, byte Kind, string? Number, string? PersonName, string? Description, decimal TotalAmount,
        byte Status, int? ReversalOfId, DateTime? ValidatedAt);
}

/// <summary>Rezultatul jobului: zilele inchise si cele lasate deschise, cu motivul (codul de eroare).</summary>
public sealed record CashAutoCloseResult(IReadOnlyList<(int TreasuryAccountId, DateOnly Day)> Closed,
    IReadOnlyList<(int TreasuryAccountId, DateOnly Day, string Reason)> LeftOpen);

/// <summary>
/// Inchiderea automata a zilelor de casa (02.02, F1 4.2; F1-A10): la 00:05, ora Romaniei, zilele deschise pana in ziua precedenta ale
/// caselor cu inchidere automata se inchid pe rand, fiecare in tranzactia ei, in numele utilizatorului tehnic (Id 1), cu
/// RegistruCasaInchis in outbox si intrare de audit. La prima zi care nu indeplineste conditiile (zi anterioara deschisa, ciorne) casa
/// ramane deschisa de acolo incolo; motivul se jurnalizeaza (notificarea casierului si contabilului vine cu fluxul 4.7 din Nucleu).
/// </summary>
public sealed class CashDayAutoClose(FinanceConnection connection, TimeProvider clock, ILogger<CashDayAutoClose> log)
{
    public const string JobId = "fin-cash-day-autoclose";

    /// <summary>Zilnic la 00:05, ora Romaniei.</summary>
    public const string Schedule = "5 0 * * *";

    private const int SystemUserId = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CashAutoCloseResult> RunAsync(CancellationToken ct)
    {
        var yesterday = BusinessDay.Today(clock).AddDays(-1);
        await using var sql = new SqlConnection(connection.ConnectionString);
        await sql.OpenAsync(ct).ConfigureAwait(false);
        var candidates = (await sql.QueryAsync<(int TreasuryAccountId, int CompanyId, string Code, DateTime Day)>(new CommandDefinition(
            "fin.usp_CashRegisterDay_ListAutoClose", new { UpTo = yesterday.ToDateTime(TimeOnly.MinValue) }, commandType: CommandType.StoredProcedure,
            cancellationToken: ct)).ConfigureAwait(false)).ToList();

        var closed = new List<(int, DateOnly)>();
        var leftOpen = new List<(int, DateOnly, string)>();
        foreach (var account in candidates.GroupBy(c => c.TreasuryAccountId))
        {
            foreach (var candidate in account)
            {
                var day = DateOnly.FromDateTime(candidate.Day);
                await using var transaction = (SqlTransaction)await sql.BeginTransactionAsync(ct).ConfigureAwait(false);
                try
                {
                    var result = await CashRegisterStore.CloseAsync(sql, transaction, candidate.CompanyId, candidate.TreasuryAccountId, day, SystemUserId, ct)
                        .ConfigureAwait(false);
                    await EnqueueAsync(sql, transaction, candidate.CompanyId, CashRegisterEvents.Closed(candidate.TreasuryAccountId, result), ct)
                        .ConfigureAwait(false);
                    await AuditAsync(sql, transaction, candidate.CompanyId, result, ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                    closed.Add((candidate.TreasuryAccountId, day));
                }
                catch (SqlException ex) when (ex.Number is 51012 or 51013 or 51005)
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    var reason = ex.Number switch { 51013 => "FIN_CASH_DAY_HAS_DRAFTS", 51012 => "FIN_CASH_PREVIOUS_DAY_OPEN", _ => "FIN_CASH_NEGATIVE_BALANCE" };
                    leftOpen.Add((candidate.TreasuryAccountId, day, reason));
                    log.LogWarning("Ziua {Day} a casei {Code} a rămas deschisă la închiderea automată: {Reason}.", day, candidate.Code, reason);
                    break;
                }
            }
        }

        log.LogInformation("Închiderea automată a caselor: {Closed} zile închise, {Open} rămase deschise.", closed.Count, leftOpen.Count);
        return new CashAutoCloseResult(closed, leftOpen);
    }

    // Aceeasi anvelopa ca IOutbox din Nucleu: tipul complet, versiunea, JSON web.
    private async Task EnqueueAsync(SqlConnection sql, SqlTransaction transaction, int companyId, RegistruCasaInchis evt, CancellationToken ct)
    {
        var envelope = evt with { OccurredAtUtc = clock.GetUtcNow().UtcDateTime, CorrelationId = Guid.NewGuid(), CompanyId = companyId };
        await sql.ExecuteAsync(new CommandDefinition("core.usp_Outbox_Enqueue", new
        {
            envelope.EventId,
            EventType = new DbString { Value = typeof(RegistruCasaInchis).FullName, Length = 200 },
            EventVersion = envelope.Version,
            CompanyId = (int?)companyId,
            envelope.CorrelationId,
            Payload = JsonSerializer.Serialize(envelope, Json),
            OccurredAt = envelope.OccurredAtUtc,
        }, transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task AuditAsync(SqlConnection sql, SqlTransaction transaction, int companyId, CashDayClosing day, CancellationToken ct) =>
        await sql.ExecuteAsync(new CommandDefinition("""
            INSERT INTO core.AuditEntry (OccurredAt, UserId, CompanyId, Action, EntityType, EntityId, Changes, IpAddress, CorrelationId)
            VALUES (@OccurredAt, 1, @CompanyId, N'Fin.Cash.CloseDay', N'Fin.CashRegisterDay', @Id, @Changes, NULL, NEWID());
            """, new
        {
            OccurredAt = clock.GetUtcNow().UtcDateTime, CompanyId = companyId, Id = (long)day.CashRegisterDayId,
            Changes = JsonSerializer.Serialize(new { day.Day, day.ClosingBalance, day.FirstPageNo, day.LastPageNo, Automatic = true }, Json),
        }, transaction, cancellationToken: ct)).ConfigureAwait(false);
}
