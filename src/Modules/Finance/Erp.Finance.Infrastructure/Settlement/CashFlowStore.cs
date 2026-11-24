using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Planning;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Planning;
using Erp.Finance.Infrastructure.Treasury;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>Prognoza de cash-flow prin fin.usp_CashFlow_ForecastInputs, istoricul de plata si sumele planificate (NET-06).</summary>
public sealed class CashFlowStore(IDbSession session) : ICashFlowStore
{
    public const string CollectionMode = "Fin.Forecast.CollectionMode";
    public const string MinSampleInvoices = "Fin.Forecast.MinSampleInvoices";
    public const string HistoryMonths = "Fin.Forecast.HistoryMonths";

    public async Task<CashFlowInputs> InputsAsync(int companyId, DateOnly start, DateOnly to, int? treasuryAccountId, string? currencyCode, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_CashFlow_ForecastInputs", new
        {
            CompanyId = companyId, Start = Day(start), To = Day(to), TreasuryAccountId = treasuryAccountId, CurrencyCode = currencyCode,
        }, ct)).ConfigureAwait(false);
        var accounts = (await grid.ReadAsync<(int Id, string Code, string Name, byte Kind, string CurrencyCode, decimal Balance)>().ConfigureAwait(false))
            .Select(a => new ForecastAccountRow(a.Id, a.Code, a.Name, a.Kind, a.CurrencyCode.Trim(), a.Balance)).ToList();
        var items = (await grid.ReadAsync<(long Id, int PartnerId, byte Role, byte Kind, string DocumentNo, DateTime DueDate, string CurrencyCode, decimal OpenAmount)>()
            .ConfigureAwait(false)).Select(i => new ForecastOpenItemRow(i.Id, i.PartnerId, i.Role, i.Kind, i.DocumentNo, DateOnly.FromDateTime(i.DueDate),
                i.CurrencyCode.Trim(), i.OpenAmount)).ToList();
        var proposals = (await grid.ReadAsync<(long LineId, string Number, DateTime Planned, int PartnerId, int AccountId, string CurrencyCode, decimal Amount)>()
            .ConfigureAwait(false)).Select(p => new ForecastProposalRow(p.LineId, p.Number, DateOnly.FromDateTime(p.Planned), p.PartnerId, p.AccountId,
                p.CurrencyCode.Trim(), p.Amount)).ToList();
        var orders = (await grid.ReadAsync<(int Id, DateTime Date, int? PartnerId, int AccountId, string CurrencyCode, decimal Amount)>().ConfigureAwait(false))
            .Select(o => new ForecastDraftOrderRow(o.Id, DateOnly.FromDateTime(o.Date), o.PartnerId, o.AccountId, o.CurrencyCode.Trim(), o.Amount)).ToList();
        var plans = (await grid.ReadAsync<PlanRow>().ConfigureAwait(false)).Select(p => new ForecastPlanRow(p.Id, p.CategoryId, p.Direction, p.Description,
            p.CurrencyCode.Trim(), p.Amount, p.TreasuryAccountId, DateOnly.FromDateTime(p.FirstDate), (Recurrence)p.Recurrence, p.RecurrenceDay,
            p.EndDate is { } e ? DateOnly.FromDateTime(e) : null, p.Probability)).ToList();
        var cards = (await grid.ReadAsync<(int Id, DateTime Date, decimal Gross)>().ConfigureAwait(false))
            .Select(c => new ForecastCardRow(c.Id, DateOnly.FromDateTime(c.Date), c.Gross)).ToList();
        var behavior = (await grid.ReadAsync<ForecastBehaviorRow>().ConfigureAwait(false)).ToList();
        return new CashFlowInputs(accounts, items, proposals, orders, plans, cards, behavior);
    }

    public async Task<IReadOnlyList<CashFlowCategoryDto>> CategoriesAsync(CancellationToken ct) =>
        (await session.Connection.QueryAsync<CashFlowCategoryDto>(Command("fin.usp_CashFlowCategory_List", null, ct)).ConfigureAwait(false)).ToList();

    public async Task<ForecastSettings> SettingsAsync(int companyId, CancellationToken ct)
    {
        var mode = await IntAsync(companyId, CollectionMode, ct).ConfigureAwait(false);
        var sample = await IntAsync(companyId, MinSampleInvoices, ct).ConfigureAwait(false);
        var months = await IntAsync(companyId, HistoryMonths, ct).ConfigureAwait(false);
        return new ForecastSettings(mode is 2 ? (byte)2 : (byte)1, sample is >= 0 and <= 1000 ? sample.Value : 5, months is >= 1 and <= 60 ? months.Value : 12);
    }

    public Task<int> RecalculateBehaviorAsync(int companyId, DateOnly asOf, int historyMonths, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("fin.usp_PartnerPaymentBehavior_Recalculate", new
        {
            CompanyId = companyId, AsOf = Day(asOf), HistoryMonths = historyMonths, UserId = userId,
        }, ct));

    public async Task<IReadOnlyList<DelayBucketDto>> BucketsAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(int Id, string Code, string Name, int? DaysFrom, int? DaysTo, byte SortOrder, bool IsUncollectible)>(
            Command("fin.usp_PaymentDelayBucket_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .Select(b => new DelayBucketDto(b.Code, b.Name, b.DaysFrom, b.DaysTo, b.IsUncollectible)).ToList();

    public Task SaveBucketsAsync(int companyId, IReadOnlyList<DelayBucketDefinition> buckets, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_PaymentDelayBucket_Save", new
        {
            CompanyId = companyId, UserId = userId,
            Buckets = JsonSerializer.Serialize(buckets.Select(b => new
            {
                code = b.Code, name = b.Name, daysFrom = b.DaysFrom, daysTo = b.DaysTo, isUncollectible = b.IsUncollectible,
            })),
        }, ct));

    public async Task<IReadOnlyList<CashFlowPlanItemDto>> PlanItemsAsync(int companyId, bool includeInactive, CancellationToken ct) =>
        (await session.Connection.QueryAsync<PlanItemRow>(Command("fin.usp_CashFlowPlanItem_List", new { CompanyId = companyId, IncludeInactive = includeInactive }, ct))
            .ConfigureAwait(false))
        .Select(p => new CashFlowPlanItemDto(p.Id, p.CategoryId, p.CategoryCode, p.CategoryName, p.Direction, p.Description, p.CurrencyCode.Trim(),
            Money.Format(p.Amount), p.TreasuryAccountId, p.TreasuryAccountCode, DateOnly.FromDateTime(p.FirstDate), (Recurrence)p.Recurrence, p.RecurrenceDay,
            p.EndDate is { } e ? DateOnly.FromDateTime(e) : null, p.IsActive, p.Probability, p.RowVersion)).ToList();

    public Task<int> SavePlanItemAsync(int companyId, CashFlowPlanItemRecord item, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("fin.usp_CashFlowPlanItem_Save", new
        {
            CompanyId = companyId, item.Id, item.CategoryId, item.Description, item.CurrencyCode, item.Amount, item.TreasuryAccountId,
            FirstDate = Day(item.FirstDate), Recurrence = (byte)item.Recurrence, item.RecurrenceDay, EndDate = item.EndDate is { } e ? Day(e) : (DateTime?)null,
            item.IsActive, item.Probability, item.RowVersion, UserId = userId,
        }, ct));

    public Task DeletePlanItemAsync(int companyId, int id, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_CashFlowPlanItem_Delete", new { CompanyId = companyId, Id = id }, ct));

    private async Task<int?> IntAsync(int companyId, string code, CancellationToken ct) =>
        int.TryParse(await session.Connection.QuerySingleOrDefaultAsync<string?>(Command("fin.usp_CompanySetting_Get", new { CompanyId = companyId, Code = code }, ct))
            .ConfigureAwait(false), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record PlanRow(int Id, int CategoryId, byte Direction, string Description, string CurrencyCode, decimal Amount, int? TreasuryAccountId,
        DateTime FirstDate, byte Recurrence, byte? RecurrenceDay, DateTime? EndDate, byte Probability);

    private sealed record PlanItemRow(int Id, int CategoryId, string CategoryCode, string CategoryName, byte Direction, string Description, string CurrencyCode,
        decimal Amount, int? TreasuryAccountId, string? TreasuryAccountCode, DateTime FirstDate, byte Recurrence, byte? RecurrenceDay, DateTime? EndDate,
        bool IsActive, byte Probability, byte[] RowVersion);
}

/// <summary>
/// Jobul lunar al istoricului de plata (02.02, F5 3.4): in prima zi a lunii, la 01:30 ora Romaniei, recalculeaza distributia pe intervale
/// pentru fiecare firma cu partide de client, fiecare in tranzactia ei, in numele utilizatorului tehnic (Id 1).
/// </summary>
public sealed class PaymentBehaviorRecalculation(FinanceConnection connection, TimeProvider clock, ILogger<PaymentBehaviorRecalculation> log)
{
    public const string JobId = "fin-payment-behavior";

    /// <summary>Lunar, pe 1, la 01:30 ora Romaniei.</summary>
    public const string Schedule = "30 1 1 * *";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        await using var sql = new SqlConnection(connection.ConnectionString);
        await sql.OpenAsync(ct).ConfigureAwait(false);
        var today = BusinessDay.Today(clock).ToDateTime(TimeOnly.MinValue);
        var companies = (await sql.QueryAsync<(int CompanyId, int HistoryMonths)>(new CommandDefinition("fin.usp_PartnerPaymentBehavior_Companies",
            commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false)).ToList();
        foreach (var (companyId, months) in companies)
        {
            var partners = await sql.QuerySingleAsync<int>(new CommandDefinition("fin.usp_PartnerPaymentBehavior_Recalculate",
                new { CompanyId = companyId, AsOf = today, HistoryMonths = months is >= 1 and <= 60 ? months : 12, UserId = 1 },
                commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
            log.LogInformation("Istoric de plată recalculat pentru firma {CompanyId}: {Partners} clienți.", companyId, partners);
        }

        return companies.Count;
    }
}
