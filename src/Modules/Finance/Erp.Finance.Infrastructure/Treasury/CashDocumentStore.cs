using System.Data;
using System.Globalization;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Treasury;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Infrastructure.Treasury;

/// <summary>Documentele de casa prin procedurile fin.usp_CashDocument_*, fin.usp_CashDay_Get si fin.usp_CashLimit* (NET-06).</summary>
public sealed class CashDocumentStore(IDbSession session) : ICashDocumentStore
{
    public async Task<CashDeskLookupsDto> GetDeskLookupsAsync(int companyId, int userId, bool viewAll, DateOnly day, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Proc("fin.usp_CashDesk_Lookups", new
        {
            CompanyId = companyId, UserId = userId, ViewAll = viewAll, Day = Date(day),
        }, ct)).ConfigureAwait(false);
        var accounts = (await grid.ReadAsync<AccountRow>().ConfigureAwait(false))
            .Select(a => new CashAccountOptionDto(a.Id, a.Code, a.Name, a.CurrencyCode.Trim(), a.CanValidate)).ToList();
        var types = (await grid.ReadAsync<OperationRow>().ConfigureAwait(false))
            .Select(t => new CashOperationTypeDto(t.Id, t.Code, t.Name, (CashDirection)t.Direction, t.RequiresPartner, t.RequiresEmployee,
                t.RequiresCounterAccount, t.CashLimitCategory)).ToList();
        var counter = (await grid.ReadAsync<TreasuryOptionDto>().ConfigureAwait(false)).ToList();
        var employees = (await grid.ReadAsync<CashEmployeeOptionDto>().ConfigureAwait(false)).ToList();
        return new CashDeskLookupsDto(accounts, types, counter, employees);
    }

    public async Task<(CashDaySummaryDto? Day, IReadOnlyList<CashDayDocumentDto> Documents)> GetDayAsync(int companyId, int treasuryAccountId, DateOnly day,
        CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Proc("fin.usp_CashDay_Get", new
        {
            CompanyId = companyId, TreasuryAccountId = treasuryAccountId, Day = Date(day),
        }, ct)).ConfigureAwait(false);
        if (grid.IsConsumed)
        {
            return (null, []);
        }

        var row = await grid.ReadSingleOrDefaultAsync<DayRow>().ConfigureAwait(false);
        if (row is null)
        {
            return (null, []);
        }

        var summary = new CashDaySummaryDto(row.TreasuryAccountId, row.Code, row.Name, row.CurrencyCode.Trim(), DateOnly.FromDateTime(row.Day),
            Money.Format(row.OpeningBalance), Money.Format(row.TotalIn), Money.Format(row.TotalOut), Money.Format(row.ClosingBalance), row.DayStatus == 2,
            Day(row.FirstOpenDay), Day(row.LastClosedDay), Money.Format(row.MaxClosingBalance));
        var documents = (await grid.ReadAsync<DayDocumentRow>().ConfigureAwait(false))
            .Select(d => new CashDayDocumentDto(d.Id, (CashDocumentKind)d.Kind, d.Number, (CashDocumentStatus)d.Status, d.PersonName, d.OperationName,
                Money.Format(d.TotalAmount), d.Description, d.ReversalOfId, d.ReversedById, d.ValidatedAt)).ToList();
        return (summary, documents);
    }

    public async Task<CashDocumentState?> GetAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Proc("fin.usp_CashDocument_Get", new { CompanyId = companyId, CashDocumentId = id }, ct))
            .ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<HeaderRow>().ConfigureAwait(false);
        if (h is null)
        {
            return null;
        }

        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false)).ToList();
        var document = new CashDocumentDetailDto(h.Id, h.TreasuryAccountId, h.TreasuryAccountCode, h.TreasuryAccountName, (CashDocumentKind)h.Kind, h.Number,
            DateOnly.FromDateTime(h.DocumentDate), (CashDocumentStatus)h.Status, h.PartnerId, h.PartnerName, h.EmployeeId, h.EmployeeName, h.PersonName,
            h.HasPersonId, h.CurrencyCode.Trim(), h.ExchangeRate.ToString("0.######", CultureInfo.InvariantCulture), DateOnly.FromDateTime(h.RateDate),
            Money.Format(h.TotalAmount), Money.Format(h.TotalAmountRon), h.Description, h.ReversalOfId, h.ReversalOfNumber, h.ReversedById,
            h.ReversedByNumber, h.ValidatedAt, h.ValidatedByName, h.CreatedAt, h.ModifiedAt, h.ModifiedByName,
            lines.Select(l => new CashDocumentLineDto(l.Id, l.LineNumber, l.TreasuryOperationTypeId, l.OperationTypeCode, l.OperationTypeName,
                (CashDirection)l.Direction, Money.Format(l.Amount), Money.Format(l.AmountRon), l.CounterTreasuryAccountId, l.CounterTreasuryAccountCode,
                l.SettlementRef, l.CostCenterId, l.ProjectId, l.Note)).ToList(),
            h.RowVersion);
        return new CashDocumentState(document, h.BranchId, h.ReceiptSeriesId, h.CashInSeriesId, h.CashOutSeriesId, h.PartnerType,
            lines.Select(l => new CashLineState(l.Id, l.OperationTypeCode, l.CashLimitCategory, l.Amount, l.AmountRon, l.SettlementRef,
                l.CounterTreasuryAccountId)).ToList());
    }

    public Task<bool> IsAssignedAsync(int treasuryAccountId, int userId, DateOnly day, bool forValidation, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<bool>(Proc("fin.usp_TreasuryAccountUser_IsAssigned", new
        {
            TreasuryAccountId = treasuryAccountId, UserId = userId, Day = Date(day), ForValidation = forValidation,
        }, ct));

    public Task<FinanceSaved> SaveAsync(CashDocumentRecord document, bool requireAssignment, int userId, CancellationToken ct)
    {
        var lines = new DataTable();
        lines.Columns.Add("LineNumber", typeof(short));
        lines.Columns.Add("TreasuryOperationTypeId", typeof(int));
        lines.Columns.Add("Amount", typeof(decimal));
        lines.Columns.Add("AmountRon", typeof(decimal));
        lines.Columns.Add("CounterTreasuryAccountId", typeof(int));
        lines.Columns.Add("SettlementRef", typeof(string));
        lines.Columns.Add("CostCenterId", typeof(int));
        lines.Columns.Add("ProjectId", typeof(int));
        lines.Columns.Add("Note", typeof(string));
        foreach (var l in document.Lines)
        {
            lines.Rows.Add(l.LineNumber, l.OperationTypeId, l.Amount, l.AmountRon, (object?)l.CounterTreasuryAccountId ?? DBNull.Value,
                (object?)l.SettlementRef ?? DBNull.Value, (object?)l.CostCenterId ?? DBNull.Value, (object?)l.ProjectId ?? DBNull.Value,
                (object?)l.Note ?? DBNull.Value);
        }

        return session.Connection.QuerySingleAsync<FinanceSaved>(Proc("fin.usp_CashDocument_Save", new
        {
            document.Id, document.CompanyId, document.TreasuryAccountId, Kind = (byte)document.Kind, DocumentDate = Date(document.DocumentDate),
            document.PartnerId, document.EmployeeId, document.PersonName, document.PersonIdEncrypted, document.KeepPersonId, document.ExchangeRate,
            RateDate = Date(document.RateDate), document.TotalAmount, document.TotalAmountRon, document.Description,
            Lines = lines.AsTableValuedParameter("fin.CashDocumentLineList"), document.RowVersion, RequireAssignment = requireAssignment, UserId = userId,
        }, ct));
    }

    public Task DeleteAsync(int companyId, int id, byte[] rowVersion, bool requireAssignment, int userId, CancellationToken ct) =>
        session.Connection.ExecuteScalarAsync<int>(Proc("fin.usp_CashDocument_Delete", new
        {
            CashDocumentId = id, CompanyId = companyId, RowVersion = rowVersion, RequireAssignment = requireAssignment, UserId = userId,
        }, ct));

    public async Task<IReadOnlyDictionary<string, decimal>> GetDayTotalsAsync(int companyId, DateOnly day, int? partnerId, int? employeeId,
        string? personName, int excludeId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(string Category, decimal Total)>(Proc("fin.usp_CashLimit_GetDayTotals", new
        {
            CompanyId = companyId, Day = Date(day), PartnerId = partnerId, EmployeeId = employeeId, PersonName = personName, ExcludeId = excludeId,
        }, ct)).ConfigureAwait(false)).ToDictionary(r => r.Category, r => r.Total, StringComparer.Ordinal);

    public async Task<IReadOnlyList<CashLimit>> GetLimitsAsync(int companyId, DateOnly day, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(string Category, decimal Amount, byte Action)>(Proc("fin.usp_CashLimitRule_ListEffective", new
        {
            CompanyId = companyId, Day = Date(day),
        }, ct)).ConfigureAwait(false)).Select(r => new CashLimit(r.Category, r.Amount, (CashLimitAction)r.Action)).ToList();

    public Task<CashValidation> ValidateAsync(int companyId, int id, string? number, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<CashValidation>(Proc("fin.usp_CashDocument_Validate", new
        {
            CashDocumentId = id, CompanyId = companyId, Number = number, RowVersion = rowVersion, UserId = userId,
        }, ct));

    public Task<CashValidation> ReverseAsync(int companyId, int id, byte[] rowVersion, DateOnly day, string? number, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<CashValidation>(Proc("fin.usp_CashDocument_Reverse", new
        {
            CashDocumentId = id, CompanyId = companyId, RowVersion = rowVersion, Day = Date(day), Number = number, UserId = userId,
        }, ct));

    private CommandDefinition Proc(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DateTime Date(DateOnly value) => value.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? Day(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;

    private sealed record AccountRow(int Id, string Code, string Name, string CurrencyCode, bool CanValidate);

    private sealed record OperationRow(int Id, string Code, string Name, byte Direction, bool RequiresPartner, bool RequiresEmployee,
        bool RequiresCounterAccount, string? CashLimitCategory);

    private sealed record DayRow(int TreasuryAccountId, string Code, string Name, string CurrencyCode, decimal? MaxClosingBalance, DateTime Day,
        decimal OpeningBalance, decimal TotalIn, decimal TotalOut, decimal ClosingBalance, byte DayStatus, DateTime? ClosedAt, DateTime? FirstOpenDay,
        DateTime? LastClosedDay);

    private sealed record DayDocumentRow(int Id, byte Kind, string? Number, DateTime DocumentDate, byte Status, string? PersonName, decimal TotalAmount,
        decimal TotalAmountRon, string? Description, int? ReversalOfId, int? ReversedById, DateTime? ValidatedAt, string? OperationName);

    private sealed record HeaderRow(int Id, int CompanyId, int TreasuryAccountId, string TreasuryAccountCode, string TreasuryAccountName, int? BranchId,
        int? ReceiptSeriesId, int? CashInSeriesId, int? CashOutSeriesId, byte Kind, string? Number, DateTime DocumentDate, byte Status, int? PartnerId,
        string? PartnerName, byte? PartnerType, int? EmployeeId, string? EmployeeName, string? PersonName, bool HasPersonId, string CurrencyCode,
        decimal ExchangeRate, DateTime RateDate, decimal TotalAmount, decimal TotalAmountRon, string? Description, int? ReversalOfId,
        string? ReversalOfNumber, int? ReversedById, string? ReversedByNumber, DateTime? ValidatedAt, string? ValidatedByName, DateTime CreatedAt,
        DateTime? ModifiedAt, string? ModifiedByName, byte[] RowVersion);

    private sealed record LineRow(long Id, short LineNumber, int TreasuryOperationTypeId, string OperationTypeCode, string OperationTypeName, byte Direction,
        string? CashLimitCategory, decimal Amount, decimal AmountRon, int? CounterTreasuryAccountId, string? CounterTreasuryAccountCode,
        string? SettlementRef, int? CostCenterId, int? ProjectId, string? Note);
}

/// <summary>
/// Jurnalul verificarilor de plafon (fin.usp_CashLimitCheck_Log). Refuzurile se scriu pe o conexiune noua, in afara tranzactiei comenzii
/// (care se anuleaza odata cu refuzul), ca raportul incercarilor de depasire sa le pastreze (F1 3.6 regula 4).
/// </summary>
public sealed class CashLimitLog(IDbSession session, FinanceConnection connection) : ICashLimitLog
{
    public async Task LogAsync(int companyId, int? cashDocumentId, int userId, CashDocumentDetailDto document, CashLimitBreach breach, bool confirmed,
        bool outsideTransaction, CancellationToken ct)
    {
        var parameters = new
        {
            CompanyId = companyId, CashDocumentId = cashDocumentId, UserId = userId, document.PartnerId, document.EmployeeId, document.PersonName,
            breach.Category, DocumentDate = document.DocumentDate.ToDateTime(TimeOnly.MinValue), breach.Amount, breach.DayTotal,
            LimitAmount = breach.Limit, Action = (byte)breach.Action, Confirmed = confirmed,
        };
        if (!outsideTransaction)
        {
            await session.Connection.ExecuteAsync(new CommandDefinition("fin.usp_CashLimitCheck_Log", parameters, session.Transaction,
                commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
            return;
        }

        await using var own = new SqlConnection(connection.ConnectionString);
        await own.OpenAsync(ct).ConfigureAwait(false);
        await own.ExecuteAsync(new CommandDefinition("fin.usp_CashLimitCheck_Log", parameters, commandType: CommandType.StoredProcedure,
            cancellationToken: ct)).ConfigureAwait(false);
    }
}

/// <summary>Sirul de conexiune al bazei, pentru scrierile autonome ale modulului (jurnalul refuzurilor de plafon).</summary>
public sealed record FinanceConnection(string ConnectionString);
