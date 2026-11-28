using System.Data;
using System.Globalization;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Costing;
using Erp.Inventory.Domain;
using Erp.Inventory.Domain.Costing;
using Erp.Inventory.Infrastructure.Movements;

namespace Erp.Inventory.Infrastructure.Costing;

/// <summary>Documentele MP si DA si metoda de descarcare prin inv.usp_RetailPriceChange_*, inv.usp_RetailDischarge_* (NET-06).</summary>
public sealed class RetailValuationStore(IDbSession session) : IRetailValuationStore
{
    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DateTime Date(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    public Task<InventorySaved> SavePriceChangeAsync(int companyId, int? id, int warehouseId, DateOnly documentDate, string? notes,
        IReadOnlyList<(int ItemVariantId, decimal NewPrice)> lines, byte[]? rowVersion, int userId, CancellationToken ct)
    {
        var table = StockMovementStore.Table(("LineNumber", typeof(int)), ("ItemVariantId", typeof(int)), ("NewPrice", typeof(decimal)));
        var no = 0;
        foreach (var l in lines)
        {
            table.Rows.Add(++no, l.ItemVariantId, l.NewPrice);
        }

        return session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_RetailPriceChange_Save", new
        {
            Id = id, CompanyId = companyId, WarehouseId = warehouseId, DocumentDate = Date(documentDate), Notes = notes,
            Lines = table.AsTableValuedParameter("inv.tt_RetailPriceChangeLine"), RowVersion = rowVersion, UserId = userId,
        }, ct));
    }

    public async Task<RetailPriceChangeDto?> GetPriceChangeAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_RetailPriceChange_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        var head = await grid.ReadSingleOrDefaultAsync<HeadRow>().ConfigureAwait(false);
        if (head is null)
        {
            return null;
        }

        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false))
            .Select(l => new RetailPriceChangeLineDto(l.LineNumber, l.ItemVariantId, l.VariantCode, l.VariantName, Quantity.Format(l.NewPrice),
                Quantity.Format(l.OldPrice), Quantity.Format(l.Qty), l.VatPercent, Money(l.DiffAmount), Money(l.DiffVatAmount)))
            .ToList();
        return new RetailPriceChangeDto(head.Id, head.Number, head.WarehouseId, head.WarehouseCode, DateOnly.FromDateTime(head.DocumentDate), head.Status,
            head.Notes, Money(head.DiffAmount), Money(head.DiffVatAmount), lines, head.RowVersion);
    }

    public async Task<IReadOnlyList<RetailPriceChangeItemDto>> ListPriceChangesAsync(int companyId, int? warehouseId, byte? status, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ItemRow>(Command("inv.usp_RetailPriceChange_List",
            new { CompanyId = companyId, WarehouseId = warehouseId, Status = status }, ct)).ConfigureAwait(false))
        .Select(r => new RetailPriceChangeItemDto(r.Id, r.Number, r.WarehouseId, r.WarehouseCode, DateOnly.FromDateTime(r.DocumentDate), r.Status,
            Money(r.DiffAmount), Money(r.DiffVatAmount), r.LineCount))
        .ToList();

    public Task<InventorySaved> ValidatePriceChangeAsync(int companyId, int id, byte[] rowVersion, string number, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_RetailPriceChange_Validate",
            new { CompanyId = companyId, Id = id, RowVersion = rowVersion, Number = number, UserId = userId }, ct));

    public Task<InventorySaved> CancelPriceChangeAsync(int companyId, int id, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_RetailPriceChange_Cancel",
            new { CompanyId = companyId, Id = id, RowVersion = rowVersion, UserId = userId }, ct));

    public async Task<RetailDischargeData> ComputeDischargeAsync(int companyId, int warehouseId, DateOnly periodStart, CancellationToken ct)
    {
        var r = await session.Connection.QuerySingleAsync<ComputeRow>(Command("inv.usp_RetailDischarge_Compute",
            new { CompanyId = companyId, WarehouseId = warehouseId, PeriodStart = Date(periodStart) }, ct)).ConfigureAwait(false);
        return new RetailDischargeData(new RetailDischargeInput(r.OpeningRetail, r.RetailIn, r.RetailOut, r.OpeningMarkup, r.MarkupIn, r.OpeningVat, r.VatIn,
            r.SalesVat), r.AlreadyDischarged, r.CostCenterId);
    }

    public Task<int> SaveDischargeAsync(int companyId, int warehouseId, DateOnly periodStart, string number, RetailDischargeInput input,
        RetailDischargeResult result, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("inv.usp_RetailDischarge_Save", new
        {
            CompanyId = companyId, WarehouseId = warehouseId, PeriodStart = Date(periodStart), Number = number, input.OpeningRetail, input.RetailIn,
            input.RetailOut, input.OpeningMarkup, input.MarkupIn, input.OpeningVat, input.VatIn, input.SalesVat, result.SalesNet, result.K, result.Markup,
            result.Cost, UserId = userId,
        }, ct));

    public async Task<IReadOnlyList<RetailDischargeDto>> ListDischargesAsync(int companyId, int? warehouseId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<DischargeRow>(Command("inv.usp_RetailDischarge_List", new { CompanyId = companyId, WarehouseId = warehouseId }, ct))
            .ConfigureAwait(false))
        .Select(d => new RetailDischargeDto(d.Id, d.Number, d.WarehouseId, DateOnly.FromDateTime(d.PeriodStart), Money(d.OpeningRetail)!, Money(d.RetailIn)!,
            Money(d.RetailOut)!, Money(d.OpeningMarkup)!, Money(d.MarkupIn)!, Money(d.OpeningVat)!, Money(d.VatIn)!, Money(d.SalesVat)!, Money(d.SalesNet)!, d.K,
            Money(d.Markup)!, Money(d.Cost)!))
        .ToList();

    public Task SetDischargeMethodAsync(int companyId, int? warehouseId, byte? method, DateOnly today, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("inv.usp_RetailDischargeMethod_Set",
            new { CompanyId = companyId, WarehouseId = warehouseId, Method = method, Today = Date(today) }, ct));

    private static string? Money(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture);

    private sealed record HeadRow(int Id, string? Number, int WarehouseId, string WarehouseCode, DateTime DocumentDate, byte Status, string? Notes,
        decimal? DiffAmount, decimal? DiffVatAmount, byte[] RowVersion);

    private sealed record LineRow(int LineNumber, int ItemVariantId, string VariantCode, string VariantName, decimal NewPrice, decimal? OldPrice, decimal? Qty,
        decimal? VatPercent, decimal? DiffAmount, decimal? DiffVatAmount);

    private sealed record ItemRow(int Id, string? Number, int WarehouseId, string WarehouseCode, int WarehouseDimensionId, DateTime DocumentDate, byte Status,
        decimal? DiffAmount, decimal? DiffVatAmount, int LineCount);

    private sealed record ComputeRow(decimal OpeningRetail, decimal RetailIn, decimal RetailOut, decimal OpeningMarkup, decimal MarkupIn, decimal OpeningVat,
        decimal VatIn, decimal SalesVat, bool AlreadyDischarged, int? CostCenterId);

    private sealed record DischargeRow(int Id, string Number, int WarehouseId, string WarehouseCode, int WarehouseDimensionId, DateTime PeriodStart,
        decimal OpeningRetail, decimal RetailIn, decimal RetailOut, decimal OpeningMarkup, decimal MarkupIn, decimal OpeningVat, decimal VatIn,
        decimal SalesVat, decimal SalesNet, decimal K, decimal Markup, decimal Cost, DateTime ValidatedAt);
}
