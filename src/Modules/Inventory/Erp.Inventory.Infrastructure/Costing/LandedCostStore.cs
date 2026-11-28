using System.Data;
using System.Globalization;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Costing;
using Erp.Inventory.Application.Movements;
using Erp.Inventory.Domain;
using Erp.Inventory.Infrastructure.Movements;
using Microsoft.Data.SqlClient;

namespace Erp.Inventory.Infrastructure.Costing;

/// <summary>
/// Documentele CA prin inv.usp_LandedCost_* (NET-06). Apelat si din portul de stoc (Achizitiile, in afara pipeline-ului Stocurilor), deci codurile
/// THROW se traduc aici (NET-09).
/// </summary>
public sealed class LandedCostStore(IDbSession session) : ILandedCostStore
{
    private static readonly InventorySqlErrors Errors = new();

    private static async Task<T> Translated<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (SqlException e) when (Errors.Errors.TryGetValue(e.Number, out var error))
        {
            throw new ErpException(error.Kind, error.Code, e.Message, e);
        }
    }

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    public Task<IReadOnlyList<LandedCostTarget>> TargetsAsync(int companyId, IReadOnlyCollection<long> ids, string? sourceModule, CancellationToken ct) =>
        Translated<IReadOnlyList<LandedCostTarget>>(async () =>
        {
            var table = StockMovementStore.Table(("Id", typeof(long)));
            foreach (var id in ids.Distinct())
            {
                table.Rows.Add(id);
            }

            return (await session.Connection.QueryAsync<TargetRow>(Command("inv.usp_LandedCost_Targets", new
            {
                CompanyId = companyId, MovementIds = table.AsTableValuedParameter("inv.BigIdList"), SourceModule = sourceModule,
            }, ct)).ConfigureAwait(false))
            .Select(r => new LandedCostTarget(r.RequestedId, r.MovementId, new CostKey(r.WarehouseId, r.DepotId, r.ItemVariantId, r.LotId), r.QtyBase,
                r.CostAmount, r.WeightKg, r.VolumeM3))
            .ToList();
        });

    public Task<InventorySaved> SaveAsync(LandedCostRecord record, int userId, CancellationToken ct) =>
        Translated(() =>
        {
            var lines = StockMovementStore.Table(("LineNumber", typeof(int)), ("MovementId", typeof(long)), ("ManualAmount", typeof(decimal)),
                ("AllocatedAmount", typeof(decimal)));
            var lineNo = 0;
            foreach (var l in record.Lines)
            {
                lines.Rows.Add(++lineNo, l.MovementId, StockMovementStore.Db(l.ManualAmount), l.AllocatedAmount);
            }

            return session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_LandedCost_Save", new
            {
                record.Id, record.CompanyId, DocumentDate = record.DocumentDate.ToDateTime(TimeOnly.MinValue),
                AccountingDate = record.AccountingDate.ToDateTime(TimeOnly.MinValue), record.SourceKind, record.SupplierInvoiceId, record.PartnerId,
                record.Amount, record.AllocationBasis, record.Notes, Lines = lines.AsTableValuedParameter("inv.tt_LandedCostLine"), record.RowVersion,
                UserId = userId,
            }, ct));
        });

    public async Task<LandedCostDto?> GetAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_LandedCost_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        var head = await grid.ReadSingleOrDefaultAsync<HeadRow>().ConfigureAwait(false);
        if (head is null)
        {
            return null;
        }

        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false))
            .Select(l => new LandedCostLineDto(l.LineNumber, l.MovementId, l.WarehouseId, l.WarehouseCode, l.ItemVariantId, l.VariantCode, l.VariantName,
                DateOnly.FromDateTime(l.MovementDate), l.SourceDocumentType, l.SourceDocumentNumber, Quantity.Format(l.QtyBase), Money(l.CostAmount),
                MoneyOptional(l.ManualAmount), Money(l.AllocatedAmount)))
            .ToList();
        return new LandedCostDto(head.Id, head.Number, DateOnly.FromDateTime(head.DocumentDate), DateOnly.FromDateTime(head.AccountingDate), head.SourceKind,
            head.SupplierInvoiceId, head.PartnerId, head.PartnerName, Money(head.Amount), head.AllocationBasis, head.Status, head.Notes,
            MoneyOptional(head.StockAmount), MoneyOptional(head.ExpenseAmount), lines, head.RowVersion);
    }

    public async Task<IReadOnlyList<LandedCostItemDto>> ListAsync(int companyId, byte? status, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ItemRow>(Command("inv.usp_LandedCost_List", new { CompanyId = companyId, Status = status }, ct))
            .ConfigureAwait(false))
        .Select(r => new LandedCostItemDto(r.Id, r.Number, DateOnly.FromDateTime(r.DocumentDate), DateOnly.FromDateTime(r.AccountingDate), r.SourceKind,
            r.SupplierInvoiceId, r.PartnerName, Money(r.Amount), r.AllocationBasis, r.Status, MoneyOptional(r.StockAmount), MoneyOptional(r.ExpenseAmount),
            r.LineCount))
        .ToList();

    public Task BeginValidationAsync(int companyId, int id, byte[]? rowVersion, CancellationToken ct) =>
        Translated(() => session.Connection.ExecuteAsync(Command("inv.usp_LandedCost_BeginValidation",
            new { CompanyId = companyId, Id = id, RowVersion = rowVersion }, ct)));

    public Task<InventorySaved> SetStatusAsync(int companyId, int id, byte status, string? number, decimal? stockAmount, decimal? expenseAmount,
        byte[]? rowVersion, int userId, CancellationToken ct) =>
        Translated(() => session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_LandedCost_SetStatus", new
        {
            CompanyId = companyId, Id = id, Status = status, Number = number, StockAmount = stockAmount, ExpenseAmount = expenseAmount, RowVersion = rowVersion,
            UserId = userId,
        }, ct)));

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string? MoneyOptional(decimal? value) => value is { } v ? Money(v) : null;

    private sealed record TargetRow(long RequestedId, long MovementId, int WarehouseId, string WarehouseCode, int? DepotId, int ItemVariantId, string VariantCode,
        int? LotId, DateTime MovementDate, decimal QtyBase, decimal CostAmount, string SourceDocumentType, string? SourceDocumentNumber, decimal? WeightKg,
        decimal? VolumeM3);

    private sealed record HeadRow(int Id, string? Number, DateTime DocumentDate, DateTime AccountingDate, byte SourceKind, long? SupplierInvoiceId, int? PartnerId,
        string? PartnerName, decimal Amount, byte AllocationBasis, byte Status, string? Notes, decimal? StockAmount, decimal? ExpenseAmount, byte[] RowVersion);

    private sealed record LineRow(int LineNumber, long MovementId, int WarehouseId, string WarehouseCode, int ItemVariantId, string VariantCode, string VariantName,
        DateTime MovementDate, string SourceDocumentType, string? SourceDocumentNumber, decimal QtyBase, decimal CostAmount, decimal? ManualAmount,
        decimal AllocatedAmount);

    private sealed record ItemRow(int Id, string? Number, DateTime DocumentDate, DateTime AccountingDate, byte SourceKind, long? SupplierInvoiceId,
        string? PartnerName, decimal Amount, byte AllocationBasis, byte Status, decimal? StockAmount, decimal? ExpenseAmount, int LineCount);
}
