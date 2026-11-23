using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Settlement;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>Partidele, alocarile si soldurile prin procedurile fin (NET-06), in tranzactia pipeline-ului sau a consumatorului.</summary>
public sealed class SettlementStore(IDbSession session) : ISettlementStore
{
    public async Task<OpenItemCreated> CreateItemAsync(OpenItemRecord item, int userId, CancellationToken ct)
    {
        var schedules = new DataTable();
        schedules.Columns.Add("RowNo", typeof(int));
        schedules.Columns.Add("DueDate", typeof(DateTime));
        schedules.Columns.Add("Amount", typeof(decimal));
        for (var i = 0; i < item.Schedules.Count; i++)
        {
            schedules.Rows.Add(i + 1, item.Schedules[i].DueDate.ToDateTime(TimeOnly.MinValue), item.Schedules[i].Amount);
        }

        var row = await session.Connection.QuerySingleAsync<(long Id, bool Created, byte AutoAllocationMode, bool IsPartnerPaymentBlocked)>(Command(
            "fin.usp_OpenItem_Create", new
            {
                item.CompanyId, item.PartnerId, Role = (byte)item.Role, Kind = (byte)item.Kind, item.SourceModule, item.SourceDocumentType, item.SourceDocumentId,
                item.SourceLineKey, item.DocumentNo, DocumentDate = Day(item.DocumentDate), AccountingDate = Day(item.AccountingDate), DueDate = Day(item.DueDate),
                item.CurrencyCode, item.ExchangeRate, item.Amount, item.AmountRon, item.VatAmount, item.VatAmountRon, item.IsVatOnCollection, item.GlAccountCode,
                EInvoiceStatus = (byte)item.EInvoiceStatus, item.EInvoiceIndexNo, item.CostCenterId, item.ProjectId, item.ReversalOfId,
                Schedules = schedules.AsTableValuedParameter("fin.OpenItemScheduleList"), UserId = userId,
            }, ct)).ConfigureAwait(false);
        return new OpenItemCreated(row.Id, row.Created, (AutoAllocationMode)row.AutoAllocationMode, row.IsPartnerPaymentBlocked);
    }

    public async Task<IReadOnlyList<OpenItemCandidate>> CandidatesAsync(int companyId, int partnerId, PartnerRole role, string currencyCode, byte side,
        IReadOnlyCollection<long>? ids, CancellationToken ct) =>
        (await session.Connection.QueryAsync<CandidateRow>(Command("fin.usp_OpenItem_Candidates", new
        {
            CompanyId = companyId, PartnerId = partnerId, Role = (byte)role, CurrencyCode = currencyCode, Side = side,
            Ids = ids is null ? null : System.Text.Json.JsonSerializer.Serialize(ids),
        }, ct)).ConfigureAwait(false))
        .Select(r => new OpenItemCandidate(r.Id, (OpenItemKind)r.Kind, r.DocumentNo, DateOnly.FromDateTime(r.DocumentDate), DateOnly.FromDateTime(r.DueDate),
            r.CurrencyCode, r.Amount, r.OpenAmount, r.IsDisputed, r.IsBlockedForPayment, (EInvoiceStatus)r.EInvoiceStatus, r.SpvExempt)).ToList();

    public async Task<AllocationOutcome> AllocateAsync(int companyId, IReadOnlyList<AllocationRequest> allocations, int userId, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("RowNo", typeof(int));
        table.Columns.Add("DebitOpenItemId", typeof(long));
        table.Columns.Add("CreditOpenItemId", typeof(long));
        table.Columns.Add("Amount", typeof(decimal));
        table.Columns.Add("AllocationDate", typeof(DateTime));
        table.Columns.Add("Source", typeof(byte));
        for (var i = 0; i < allocations.Count; i++)
        {
            var a = allocations[i];
            table.Rows.Add(i + 1, a.DebitOpenItemId, a.CreditOpenItemId, a.Amount, a.AllocationDate is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value,
                (byte)a.Source);
        }

        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_Allocation_CreateBatch",
            new { CompanyId = companyId, Allocations = table.AsTableValuedParameter("fin.AllocationList"), UserId = userId }, ct)).ConfigureAwait(false);
        return await ReadOutcomeAsync(grid).ConfigureAwait(false);
    }

    public async Task<AllocationOutcome> UndoAsync(int companyId, IReadOnlyCollection<long> allocationIds, string? reason, bool allowDocumentSource, DateOnly today,
        int userId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_Allocation_Undo", new
        {
            CompanyId = companyId, AllocationIds = System.Text.Json.JsonSerializer.Serialize(allocationIds), Reason = reason, AllowDocumentSource = allowDocumentSource,
            Today = Day(today), UserId = userId,
        }, ct)).ConfigureAwait(false);
        return await ReadOutcomeAsync(grid).ConfigureAwait(false);
    }

    public async Task<(IReadOnlyList<PaymentOpenItem> Items, IReadOnlyList<ActiveAllocation> Allocations)> PaymentItemsAsync(int companyId,
        string sourceDocumentType, long sourceDocumentId, string? sourceLineKey, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_OpenItem_PaymentItems", new
        {
            CompanyId = companyId, SourceDocumentType = sourceDocumentType, SourceDocumentId = sourceDocumentId, SourceLineKey = sourceLineKey,
        }, ct)).ConfigureAwait(false);
        var items = (await grid.ReadAsync<PaymentRow>().ConfigureAwait(false))
            .Select(r => new PaymentOpenItem(r.Id, r.PartnerId, (PartnerRole)r.Role, (OpenItemKind)r.Kind, r.SourceModule, r.SourceLineKey, r.DocumentNo,
                DateOnly.FromDateTime(r.DocumentDate), DateOnly.FromDateTime(r.AccountingDate), r.CurrencyCode, r.ExchangeRate, r.Amount, r.AmountRon,
                r.IsReversed)).ToList();
        var allocations = (await grid.ReadAsync<(long Id, long DebitOpenItemId, long CreditOpenItemId, DateTime AllocationDate, byte Source)>().ConfigureAwait(false))
            .Select(a => new ActiveAllocation(a.Id, a.DebitOpenItemId, a.CreditOpenItemId, DateOnly.FromDateTime(a.AllocationDate), (AllocationSource)a.Source))
            .ToList();
        return (items, allocations);
    }

    public async Task<IReadOnlyList<OpenItemListDto>> ListOpenAsync(int companyId, int partnerId, PartnerRole? role, CancellationToken ct) =>
        (await session.Connection.QueryAsync<OpenRow>(Command("fin.usp_OpenItem_ListOpen",
            new { CompanyId = companyId, PartnerId = partnerId, Role = (byte?)role }, ct)).ConfigureAwait(false))
        .Select(r => new OpenItemListDto(r.OpenItemId, (PartnerRole)r.Role, (OpenItemKind)r.Kind, r.SourceDocumentType, r.SourceDocumentId, r.DocumentNo,
            DateOnly.FromDateTime(r.DocumentDate), DateOnly.FromDateTime(r.DueDate), r.CurrencyCode, Money.Format(r.Amount), Money.Format(r.OpenAmount), r.IsDisputed,
            r.DisputeNote, r.IsBlockedForPayment, (EInvoiceStatus)r.EInvoiceStatus, r.IsVatOnCollection, r.RowVersion)).ToList();

    public async Task<IReadOnlyList<PartnerBalanceDto>> BalanceAsync(int companyId, int partnerId, PartnerRole? role, DateOnly asOf, CancellationToken ct) =>
        (await session.Connection.QueryAsync<BalanceRow>(Command("fin.usp_PartnerBalance_Get",
            new { CompanyId = companyId, PartnerId = partnerId, Role = (byte?)role, AsOf = Day(asOf) }, ct)).ConfigureAwait(false))
        .Select(r => new PartnerBalanceDto(r.CompanyId, r.PartnerId, (PartnerRole)r.Role, r.CurrencyCode, r.Balance, r.BalanceRon, r.OverdueRon, r.MaxOverdueDays,
            r.UnallocatedPaymentsRon, asOf)).ToList();

    public Task<byte[]> SetFlagsAsync(int companyId, long id, bool? isDisputed, string? disputeNote, bool? isBlockedForPayment, byte[] rowVersion, int userId,
        CancellationToken ct) =>
        session.Connection.QuerySingleAsync<byte[]>(Command("fin.usp_OpenItem_SetFlags", new
        {
            CompanyId = companyId, Id = id, IsDisputed = isDisputed, DisputeNote = disputeNote, IsBlockedForPayment = isBlockedForPayment, RowVersion = rowVersion,
            UserId = userId,
        }, ct));

    public async Task<PartnerFinanceProfileDto> GetProfileAsync(int companyId, int partnerId, PartnerRole role, CancellationToken ct)
    {
        var r = await session.Connection.QuerySingleAsync<ProfileRow>(Command("fin.usp_PartnerFinanceProfile_Get",
            new { CompanyId = companyId, PartnerId = partnerId, Role = (byte)role }, ct)).ConfigureAwait(false);
        return new PartnerFinanceProfileDto(r.PartnerId, (PartnerRole)r.Role, (AutoAllocationMode)r.AutoAllocationMode,
            r.PenaltyRatePerDay.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), r.PenaltyGraceDays, r.PenaltyCapAtPrincipal,
            r.ApplyFixedRecoveryCost, r.IsPaymentBlocked, r.BlockReason, r.BalanceConfirmationEmail, r.RowVersion);
    }

    public Task<byte[]> SaveProfileAsync(int companyId, PartnerFinanceProfileRecord profile, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<byte[]>(Command("fin.usp_PartnerFinanceProfile_Save", new
        {
            CompanyId = companyId, profile.PartnerId, Role = (byte)profile.Role, AutoAllocationMode = (byte)profile.AutoAllocationMode, profile.PenaltyRatePerDay,
            profile.PenaltyGraceDays, profile.PenaltyCapAtPrincipal, profile.ApplyFixedRecoveryCost, profile.IsPaymentBlocked, profile.BlockReason,
            profile.BalanceConfirmationEmail, profile.RowVersion, UserId = userId,
        }, ct));

    public Task<long?> FindBySourceAsync(string sourceDocumentType, long sourceDocumentId, PartnerRole role, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<long?>(Command("fin.usp_OpenItem_FindBySource",
            new { SourceDocumentType = sourceDocumentType, SourceDocumentId = sourceDocumentId, Role = (byte)role }, ct));

    public async Task<DateOnly?> AllocationDateAsync(int companyId, long allocationId, CancellationToken ct) =>
        await session.Connection.QuerySingleOrDefaultAsync<DateTime?>(Command("fin.usp_Allocation_Date", new { CompanyId = companyId, AllocationId = allocationId }, ct))
            .ConfigureAwait(false) is { } date ? DateOnly.FromDateTime(date) : null;

    public async Task<IReadOnlyDictionary<string, (byte SettlementKind, byte Direction)>> OperationTypeKindsAsync(CancellationToken ct) =>
        (await session.Connection.QueryAsync<(string Code, byte SettlementKind, byte Direction)>(Command("fin.usp_TreasuryOperationType_Kinds", new { }, ct))
            .ConfigureAwait(false)).ToDictionary(t => t.Code, t => (t.SettlementKind, t.Direction), StringComparer.Ordinal);

    private static async Task<AllocationOutcome> ReadOutcomeAsync(SqlMapper.GridReader grid)
    {
        var allocations = (await grid.ReadAsync<AllocationRow>().ConfigureAwait(false))
            .Select(a => new AllocationMade(a.AllocationId, a.PartnerId, (PartnerRole)a.Role,
                new AllocatedOpenItem(a.DebitOpenItemId, a.DebitSourceDocumentType, a.DebitSourceDocumentId, a.DebitCurrencyCode, a.DebitExchangeRate),
                new AllocatedOpenItem(a.CreditOpenItemId, a.CreditSourceDocumentType, a.CreditSourceDocumentId, a.CreditCurrencyCode, a.CreditExchangeRate),
                a.Amount, a.AmountRonDebit, a.AmountRonCredit, DateOnly.FromDateTime(a.AllocationDate), (AllocationSource)a.Source)).ToList();
        var changed = (await grid.ReadAsync<(long Id, int PartnerId, byte Role, byte Kind, string SourceDocumentType, long SourceDocumentId, DateTime? ClosedAt)>()
            .ConfigureAwait(false))
            .Select(c => new ChangedOpenItem(c.Id, c.PartnerId, (PartnerRole)c.Role, (OpenItemKind)c.Kind, c.SourceDocumentType, c.SourceDocumentId,
                c.ClosedAt is { } d ? DateOnly.FromDateTime(d) : null)).ToList();
        var vat = (await grid.ReadAsync<VatRow>().ConfigureAwait(false))
            .Select(v => new VatCashEntryMade(v.Id, v.OpenItemId, v.SourceDocumentType, v.SourceDocumentId, v.Side, DateOnly.FromDateTime(v.ExigibilityDate),
                v.BaseAmountRon, v.VatAmountRon, v.IsReversal)).ToList();
        return new AllocationOutcome(allocations, changed, vat);
    }

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record CandidateRow(long Id, byte Kind, string DocumentNo, DateTime DocumentDate, DateTime DueDate, string CurrencyCode, decimal Amount,
        decimal OpenAmount, bool IsDisputed, bool IsBlockedForPayment, byte EInvoiceStatus, bool SpvExempt);

    private sealed record AllocationRow(long AllocationId, int RowNo, int PartnerId, byte Role, long DebitOpenItemId, string DebitSourceDocumentType,
        long DebitSourceDocumentId, string DebitCurrencyCode, decimal DebitExchangeRate, long CreditOpenItemId, string CreditSourceDocumentType,
        long CreditSourceDocumentId, string CreditCurrencyCode, decimal CreditExchangeRate, decimal Amount, decimal AmountRonDebit, decimal AmountRonCredit,
        DateTime AllocationDate, byte Source);

    private sealed record VatRow(long Id, long OpenItemId, string SourceDocumentType, long SourceDocumentId, byte Side, DateTime ExigibilityDate,
        decimal BaseAmountRon, decimal VatAmountRon, bool IsReversal);

    private sealed record PaymentRow(long Id, int PartnerId, byte Role, byte Kind, string SourceModule, string SourceLineKey, string DocumentNo, DateTime DocumentDate,
        DateTime AccountingDate, string CurrencyCode, decimal ExchangeRate, decimal Amount, decimal AmountRon, bool IsReversed);

    private sealed record OpenRow(long OpenItemId, byte Role, byte Kind, string SourceDocumentType, long SourceDocumentId, string DocumentNo, DateTime DocumentDate,
        DateTime DueDate, string CurrencyCode, decimal Amount, decimal OpenAmount, bool IsDisputed, string? DisputeNote, bool IsBlockedForPayment, byte EInvoiceStatus,
        bool IsVatOnCollection, byte[] RowVersion);

    private sealed record BalanceRow(int CompanyId, int PartnerId, byte Role, string CurrencyCode, decimal Balance, decimal BalanceRon, decimal OverdueRon,
        int MaxOverdueDays, decimal UnallocatedPaymentsRon);

    private sealed record ProfileRow(int PartnerId, byte Role, byte AutoAllocationMode, decimal PenaltyRatePerDay, int PenaltyGraceDays, bool PenaltyCapAtPrincipal,
        bool ApplyFixedRecoveryCost, bool IsPaymentBlocked, string? BlockReason, string? BalanceConfirmationEmail, byte[]? RowVersion);
}

/// <summary>Soldurile si partidele pentru celelalte module (F3 6), prin aceleasi proceduri.</summary>
public sealed class PartnerBalanceQueries(ISettlementStore store) : IPartnerBalanceQueries
{
    public Task<IReadOnlyList<PartnerBalanceDto>> GetBalanceAsync(int companyId, int partnerId, PartnerRole? role, DateOnly asOf, CancellationToken ct) =>
        store.BalanceAsync(companyId, partnerId, role, asOf, ct);

    public async Task<IReadOnlyList<OpenItemDto>> GetOpenItemsAsync(int companyId, int partnerId, PartnerRole role, CancellationToken ct) =>
        (await store.ListOpenAsync(companyId, partnerId, role, ct).ConfigureAwait(false))
        .Select(i => new OpenItemDto(i.OpenItemId, i.SourceDocumentType, i.SourceDocumentId, i.DocumentNo, i.DocumentDate, i.DueDate, i.CurrencyCode,
            Money.ParseOptional(i.Amount) ?? 0, Money.ParseOptional(i.OpenAmount) ?? 0, i.IsDisputed, i.IsBlockedForPayment, (byte)i.EInvoiceStatus)).ToList();
}
