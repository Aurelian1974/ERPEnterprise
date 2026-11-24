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
            r.CurrencyCode, r.Amount, r.OpenAmount, r.IsDisputed, r.IsBlockedForPayment, (EInvoiceStatus)r.EInvoiceStatus, r.SpvExempt) { IsIndexed = r.IsIndexed })
            .ToList();

    public async Task<AllocationOutcome> AllocateAsync(int companyId, IReadOnlyList<PlannedAllocation> allocations, int userId, CancellationToken ct)
    {
        using var table = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
        {
            ("RowNo", typeof(int)), ("DebitOpenItemId", typeof(long)), ("CreditOpenItemId", typeof(long)), ("DebitAmount", typeof(decimal)),
            ("CreditAmount", typeof(decimal)), ("AmountRonDebit", typeof(decimal)), ("AmountRonCredit", typeof(decimal)), ("AllocationDate", typeof(DateTime)),
            ("Source", typeof(byte)), ("CompensationId", typeof(int)), ("WriteOffId", typeof(int)), ("CommercialPaperId", typeof(int)),
            ("CrossRate", typeof(decimal)), ("CrossRateSource", typeof(byte)), ("DifferenceRon", typeof(decimal)), ("FxOpenItemId", typeof(long)),
            ("IsIndexed", typeof(bool)), ("IndexedAmount", typeof(decimal)),
        })
        {
            table.Columns.Add(name, type);
        }

        for (var i = 0; i < allocations.Count; i++)
        {
            var (a, date, fx) = allocations[i];
            table.Rows.Add(i + 1, a.DebitOpenItemId, a.CreditOpenItemId, fx.DebitAmount, fx.CreditAmount, fx.AmountRonDebit, fx.AmountRonCredit, Day(date),
                (byte)a.Source, (object?)a.CompensationId ?? DBNull.Value, (object?)a.WriteOffId ?? DBNull.Value, (object?)a.CommercialPaperId ?? DBNull.Value,
                (object?)fx.CrossRate ?? DBNull.Value, fx.CrossRateSource is { } source ? (byte)source : DBNull.Value, fx.DifferenceRon,
                (object?)fx.FxOpenItemId ?? DBNull.Value, fx.IsIndexed, (object?)fx.IndexedAmount ?? DBNull.Value);
        }

        using var corrections = new DataTable();
        corrections.Columns.Add("RowNo", typeof(int));
        corrections.Columns.Add("OpenItemId", typeof(long));
        corrections.Columns.Add("RunId", typeof(int));
        corrections.Columns.Add("ForeignAmount", typeof(decimal));
        corrections.Columns.Add("DifferenceRon", typeof(decimal));
        for (var i = 0; i < allocations.Count; i++)
        {
            foreach (var c in allocations[i].Fx.Corrections)
            {
                corrections.Rows.Add(i + 1, c.OpenItemId, c.RunId, c.ForeignAmount, c.DifferenceRon);
            }
        }

        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_Allocation_CreateBatch", new
        {
            CompanyId = companyId, Allocations = table.AsTableValuedParameter("fin.AllocationList"),
            Corrections = corrections.AsTableValuedParameter("fin.FxCorrectionList"), UserId = userId,
        }, ct)).ConfigureAwait(false);
        return await ReadOutcomeAsync(grid).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FxLockedItem>> LockForAllocationAsync(int companyId, IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_OpenItem_LockForAllocation",
            new { CompanyId = companyId, Ids = System.Text.Json.JsonSerializer.Serialize(ids) }, ct)).ConfigureAwait(false);
        var items = (await grid.ReadAsync<LockedRow>().ConfigureAwait(false)).ToList();
        var runs = (await grid.ReadAsync<(long OpenItemId, int RunId, DateTime RevaluationDate, decimal AppliedDifferenceRon, decimal AppliedOn)>().ConfigureAwait(false))
            .ToLookup(r => r.OpenItemId, r => new FxRunShare(r.RunId, DateOnly.FromDateTime(r.RevaluationDate), r.AppliedDifferenceRon, r.AppliedOn));
        return items.Select(r => new FxLockedItem(new FxItem(r.Id, (PartnerRole)r.Role, (OpenItemKind)r.Kind, r.CurrencyCode.Trim(), r.ExchangeRate, r.OpenAmount,
            r.OpenAmountRon) { IndexCurrencyCode = r.IndexCurrencyCode?.Trim(), IndexedOpenAmount = r.IndexedOpenAmount ?? 0 }, r.PartnerId,
            DateOnly.FromDateTime(r.DocumentDate), (CrossRateRule)r.CrossCurrencyRule, r.CrossCurrencyMarkupPercent)
        {
            Runs = runs[r.Id].ToList(), IndexRate = r.IndexRate ?? 0,
        }).ToList();
    }

    public Task SetIndexAsync(int companyId, long openItemId, string indexCurrencyCode, decimal indexedAmount, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_OpenItem_SetIndex",
            new { CompanyId = companyId, Id = openItemId, IndexCurrencyCode = indexCurrencyCode, IndexedAmount = indexedAmount }, ct));

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

    public async Task<IReadOnlyList<OpenItemListDto>> ListOpenAsync(int companyId, int partnerId, PartnerRole? role, CancellationToken ct, bool onlyOpen = true,
        DateOnly? from = null, DateOnly? to = null) =>
        (await session.Connection.QueryAsync<OpenRow>(Command("fin.usp_OpenItem_ListOpen", new
        {
            CompanyId = companyId, PartnerId = partnerId, Role = (byte?)role, OnlyOpen = onlyOpen, From = from is { } f ? Day(f) : (DateTime?)null,
            To = to is { } t ? Day(t) : (DateTime?)null,
        }, ct)).ConfigureAwait(false))
        .Select(r => new OpenItemListDto(r.OpenItemId, (PartnerRole)r.Role, (OpenItemKind)r.Kind, r.SourceDocumentType, r.SourceDocumentId, r.DocumentNo,
            DateOnly.FromDateTime(r.DocumentDate), DateOnly.FromDateTime(r.DueDate), r.CurrencyCode, Money.Format(r.Amount), Money.Format(r.OpenAmount), r.IsDisputed,
            r.DisputeNote, r.IsBlockedForPayment, (EInvoiceStatus)r.EInvoiceStatus, r.IsVatOnCollection, Money.Format(r.VatAmount), Money.Format(r.VatNonExigibleRon),
            r.ClosedAt is { } c ? DateOnly.FromDateTime(c) : null, r.RowVersion)).ToList();

    public async Task<PartnerBalanceListDto> ListBalancesAsync(int companyId, PartnerBalanceFilter filter, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_PartnerBalance_List", new
        {
            CompanyId = companyId, AsOf = Day(filter.AsOf), Role = (byte?)filter.Role, filter.Search, filter.OnlyOverdue, filter.OnlyUnallocated, filter.BothRoles,
        }, ct)).ConfigureAwait(false);
        var rows = (await grid.ReadAsync<BalanceListRow>().ConfigureAwait(false))
            .Select(r => new PartnerBalanceRowDto(r.PartnerId, r.PartnerCode, r.PartnerName, r.FiscalCode, (PartnerRole)r.Role, r.HasOtherRole, Money.Format(r.BalanceRon),
                Money.Format(r.OverdueRon), Money.Format(r.CurrentRon), Money.Format(r.Days30Ron), Money.Format(r.Days60Ron), Money.Format(r.Over60Ron),
                r.MaxOverdueDays, Money.Format(r.UnallocatedRon), r.UnallocatedCount)).ToList();
        var t = await grid.ReadSingleAsync<TotalsRow>().ConfigureAwait(false);
        return new PartnerBalanceListDto(filter.AsOf, new PartnerBalanceTotalsDto(Money.Format(t.ReceivablesRon), t.CustomersWithBalance,
            Money.Format(t.OverdueReceivablesRon), t.CustomersOverdue, Money.Format(t.PayablesRon), t.SuppliersWithBalance, Money.Format(t.UnallocatedRon),
            t.UnallocatedCount, Money.Format(t.VatNonExigibleCollectedRon), Money.Format(t.VatNonExigibleDeductibleRon)), rows);
    }

    public async Task<IReadOnlyList<PartnerAllocationDto>> ListAllocationsAsync(int companyId, int partnerId, PartnerRole? role, CancellationToken ct) =>
        (await session.Connection.QueryAsync<PartnerAllocationRow>(Command("fin.usp_Allocation_ListForPartner",
            new { CompanyId = companyId, PartnerId = partnerId, Role = (byte?)role }, ct)).ConfigureAwait(false))
        .Select(a => new PartnerAllocationDto(a.Id, (PartnerRole)a.Role, DateOnly.FromDateTime(a.AllocationDate), (AllocationSource)a.Source, a.Status,
            Money.Format(a.Amount), a.CurrencyCode, a.DebitOpenItemId, a.DebitDocumentNo, (OpenItemKind)a.DebitKind, a.CreditOpenItemId, a.CreditDocumentNo,
            (OpenItemKind)a.CreditKind, a.UndoneAt, a.UndoReason, a.CreatedAt, a.CreatedByName)).ToList();

    public async Task<IReadOnlyList<InvoiceToPay>> InvoicesToPayAsync(int companyId, IReadOnlyCollection<long> ids, CancellationToken ct) =>
        (await session.Connection.QueryAsync<InvoiceToPayRow>(Command("fin.usp_OpenItem_ForPaymentOrders",
            new { CompanyId = companyId, Ids = System.Text.Json.JsonSerializer.Serialize(ids) }, ct)).ConfigureAwait(false))
        .Select(r => new InvoiceToPay(r.OpenItemId, r.PartnerId, r.PartnerName, (PartnerRole)r.Role, (OpenItemKind)r.Kind, r.DocumentNo, r.CurrencyCode, r.OpenAmount,
            r.IsDisputed, r.IsBlockedForPayment, r.PartnerBlocked, r.PartnerBankAccountId)).ToList();

    public async Task<IReadOnlyList<DueScheduleDayDto>> DueScheduleAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(DateTime? DueDay, int ReceivableCount, decimal ReceivableRon, int PayableCount, decimal PayableRon)>(Command(
            "fin.usp_DueSchedule_Report", new { CompanyId = companyId, From = Day(from), To = Day(to) }, ct)).ConfigureAwait(false))
        .Select(d => new DueScheduleDayDto(d.DueDay is { } day ? DateOnly.FromDateTime(day) : null, d.ReceivableCount, Money.Format(d.ReceivableRon), d.PayableCount,
            Money.Format(d.PayableRon))).ToList();

    public async Task<IReadOnlyList<DueItemDto>> DueItemsAsync(int companyId, PartnerRole role, DateOnly? day, DateOnly? before, CancellationToken ct) =>
        (await session.Connection.QueryAsync<DueItemRow>(Command("fin.usp_DueSchedule_Items", new
        {
            CompanyId = companyId, Role = (byte)role, Day = day is { } d ? Day(d) : (DateTime?)null, Before = before is { } b ? Day(b) : (DateTime?)null,
        }, ct)).ConfigureAwait(false))
        .Select(r => new DueItemDto(r.OpenItemId, r.PartnerId, r.PartnerName, r.AnafInactive, r.DocumentNo, DateOnly.FromDateTime(r.DocumentDate),
            DateOnly.FromDateTime(r.DueDate), r.CurrencyCode, Money.Format(r.OpenAmount), Money.Format(r.OpenRon), r.IsDisputed, r.IsBlockedForPayment,
            r.PartnerBlocked, (EInvoiceStatus)r.EInvoiceStatus, r.SpvExempt, r.PartnerBankAccountId)).ToList();

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

    public async Task<WriteOffCreated> CreateWriteOffAsync(int companyId, long openItemId, decimal? amount, WriteOffReason reason, string? note,
        DateOnly documentDate, decimal maxAmountRon, bool allowAboveThreshold, int userId, CancellationToken ct)
    {
        var r = await session.Connection.QuerySingleAsync<(int WriteOffId, long WriteOffOpenItemId, decimal Amount, bool ItemIsDebit)>(Command(
            "fin.usp_WriteOff_Create", new
            {
                CompanyId = companyId, OpenItemId = openItemId, Amount = amount, Reason = (byte)reason, Note = note, DocumentDate = Day(documentDate),
                MaxAmountRon = maxAmountRon, AllowAboveThreshold = allowAboveThreshold, UserId = userId,
            }, ct)).ConfigureAwait(false);
        return new WriteOffCreated(r.WriteOffId, r.WriteOffOpenItemId, r.Amount, r.ItemIsDebit);
    }

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
                a.Amount, a.AmountRonDebit, a.AmountRonCredit, DateOnly.FromDateTime(a.AllocationDate), (AllocationSource)a.Source) { CreditAmount = a.CreditAmount })
            .ToList();
        var changed = (await grid.ReadAsync<(long Id, int PartnerId, byte Role, byte Kind, string SourceDocumentType, long SourceDocumentId, DateTime? ClosedAt)>()
            .ConfigureAwait(false))
            .Select(c => new ChangedOpenItem(c.Id, c.PartnerId, (PartnerRole)c.Role, (OpenItemKind)c.Kind, c.SourceDocumentType, c.SourceDocumentId,
                c.ClosedAt is { } d ? DateOnly.FromDateTime(d) : null)).ToList();
        var vat = (await grid.ReadAsync<VatRow>().ConfigureAwait(false))
            .Select(v => new VatCashEntryMade(v.Id, v.OpenItemId, v.SourceDocumentType, v.SourceDocumentId, v.Side, DateOnly.FromDateTime(v.ExigibilityDate),
                v.BaseAmountRon, v.VatAmountRon, v.IsReversal)).ToList();
        var fx = (await grid.ReadAsync<FxRow>().ConfigureAwait(false))
            .Select(x => new FxDifferenceMade(x.Id, DateOnly.FromDateTime(x.DifferenceDate), x.Source, x.AllocationId, x.PartnerId, (PartnerRole?)x.Role, x.OpenItemId,
                x.CurrencyCode, x.ForeignAmount, x.DifferenceRon, x.IsIndexed, x.GlAccountCode, x.IsReversal)).ToList();
        return new AllocationOutcome(allocations, changed, vat, fx);
    }

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record CandidateRow(long Id, byte Kind, string DocumentNo, DateTime DocumentDate, DateTime DueDate, string CurrencyCode, decimal Amount,
        decimal OpenAmount, bool IsDisputed, bool IsBlockedForPayment, byte EInvoiceStatus, bool SpvExempt, bool IsIndexed);

    private sealed record AllocationRow(long AllocationId, int RowNo, int PartnerId, byte Role, long DebitOpenItemId, string DebitSourceDocumentType,
        long DebitSourceDocumentId, string DebitCurrencyCode, decimal DebitExchangeRate, long CreditOpenItemId, string CreditSourceDocumentType,
        long CreditSourceDocumentId, string CreditCurrencyCode, decimal CreditExchangeRate, decimal Amount, decimal AmountRonDebit, decimal AmountRonCredit,
        DateTime AllocationDate, byte Source, decimal CreditAmount);

    private sealed record FxRow(long Id, DateTime DifferenceDate, byte Source, long? AllocationId, int? PartnerId, byte? Role, long? OpenItemId, string CurrencyCode,
        decimal ForeignAmount, decimal DifferenceRon, bool IsIndexed, string GlAccountCode, bool IsReversal);

    private sealed record LockedRow(long Id, int PartnerId, byte Role, byte Kind, string CurrencyCode, decimal ExchangeRate, decimal OpenAmount, decimal OpenAmountRon,
        DateTime DocumentDate, byte CrossCurrencyRule, decimal CrossCurrencyMarkupPercent, string? IndexCurrencyCode, decimal? IndexedOpenAmount,
        decimal? IndexRate);

    private sealed record VatRow(long Id, long OpenItemId, string SourceDocumentType, long SourceDocumentId, byte Side, DateTime ExigibilityDate,
        decimal BaseAmountRon, decimal VatAmountRon, bool IsReversal);

    private sealed record PaymentRow(long Id, int PartnerId, byte Role, byte Kind, string SourceModule, string SourceLineKey, string DocumentNo, DateTime DocumentDate,
        DateTime AccountingDate, string CurrencyCode, decimal ExchangeRate, decimal Amount, decimal AmountRon, bool IsReversed);

    private sealed record OpenRow(long OpenItemId, byte Role, byte Kind, string SourceDocumentType, long SourceDocumentId, string DocumentNo, DateTime DocumentDate,
        DateTime DueDate, string CurrencyCode, decimal Amount, decimal OpenAmount, bool IsDisputed, string? DisputeNote, bool IsBlockedForPayment, byte EInvoiceStatus,
        bool IsVatOnCollection, decimal VatAmount, decimal VatNonExigibleRon, DateTime? ClosedAt, byte[] RowVersion);

    private sealed record BalanceListRow(int PartnerId, string PartnerCode, string PartnerName, string? FiscalCode, byte Role, bool HasOtherRole, decimal BalanceRon,
        decimal OverdueRon, decimal CurrentRon, decimal Days30Ron, decimal Days60Ron, decimal Over60Ron, int MaxOverdueDays, decimal UnallocatedRon, int UnallocatedCount);

    private sealed record TotalsRow(decimal ReceivablesRon, int CustomersWithBalance, decimal OverdueReceivablesRon, int CustomersOverdue, decimal PayablesRon,
        int SuppliersWithBalance, decimal UnallocatedRon, int UnallocatedCount, decimal VatNonExigibleCollectedRon, decimal VatNonExigibleDeductibleRon);

    private sealed record InvoiceToPayRow(long OpenItemId, int PartnerId, string PartnerName, byte Role, byte Kind, string DocumentNo, string CurrencyCode,
        decimal OpenAmount, bool IsDisputed, bool IsBlockedForPayment, bool PartnerBlocked, int? PartnerBankAccountId);

    private sealed record DueItemRow(long OpenItemId, int PartnerId, string PartnerName, bool AnafInactive, string DocumentNo, DateTime DocumentDate,
        DateTime DueDate, string CurrencyCode, decimal OpenAmount, decimal OpenRon, bool IsDisputed, bool IsBlockedForPayment, bool PartnerBlocked,
        byte EInvoiceStatus, bool SpvExempt, int? PartnerBankAccountId);

    private sealed record PartnerAllocationRow(long Id, byte Role, DateTime AllocationDate, byte Source, byte Status, decimal Amount, string CurrencyCode,
        long DebitOpenItemId, string DebitDocumentNo, byte DebitKind, long CreditOpenItemId, string CreditDocumentNo, byte CreditKind, DateTime? UndoneAt,
        string? UndoReason, DateTime CreatedAt, string? CreatedByName);

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
