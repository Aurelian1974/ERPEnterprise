using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application.Movements;
using Microsoft.Data.SqlClient;

namespace Erp.Inventory.Infrastructure.Movements;

/// <summary>
/// Registrul de miscari prin inv.usp_StockMovement_Post, _Reverse si inv.usp_Stock_Availability (NET-06), cu TVP (SP-07). Portul e apelat si
/// din afara pipeline-ului comenzilor (consumatori, alte module), deci codurile THROW ale Stocurilor se traduc aici (NET-09).
/// </summary>
public sealed class StockMovementStore(IDbSession session) : IStockMovementStore
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

    public async Task<BackdatingSettings> GetBackdatingAsync(int companyId, CancellationToken ct) =>
        await session.Connection.QuerySingleOrDefaultAsync<BackdatingSettings>(new CommandDefinition("""
            SELECT BackdatingDays, BackdatingWithRight FROM inv.StockSettings WHERE CompanyId = @CompanyId;
            """, new { CompanyId = companyId }, session.Transaction, cancellationToken: ct)).ConfigureAwait(false)
        ?? new BackdatingSettings(5, true);

    public async Task<StockPostingOutcome> PostAsync(StockPostingRequest request, int? employeeId, int userId, CancellationToken ct)
    {
        var lines = Requests();
        var row = 0;
        var rowBySource = request.Lines.Select(l => (l.SourceLineId, l.SourceRole)).ToList();
        foreach (var l in request.Lines)
        {
            row++;
            // Intrarea unui transfer (SourceRole 2) are perechea in iesirea aceleiasi linii sursa (SourceRole 1).
            var counter = l.SourceRole == 2 ? rowBySource.IndexOf((l.SourceLineId, (byte)1)) + 1 : 0;
            lines.Rows.Add(row, l.SourceLineId, l.SourceRole, l.MovementTypeCode, (object?)l.DepotId ?? DBNull.Value, l.WarehouseId, l.ItemVariantId,
                l.QtyBase, (object?)l.InboundUnitCost ?? DBNull.Value, (object?)l.LotId ?? DBNull.Value, (object?)l.SerialId ?? DBNull.Value,
                (object?)l.CostCenterId ?? DBNull.Value, (object?)l.ProjectId ?? DBNull.Value, (object?)l.ReasonId ?? DBNull.Value,
                counter > 0 ? counter : DBNull.Value, DBNull.Value, DBNull.Value);
        }

        var rows = await Translated(() => session.Connection.QueryAsync<Row>(new CommandDefinition("inv.usp_StockMovement_Post", new
        {
            request.CompanyId, request.SourceModule, request.SourceDocumentType, request.SourceDocumentId, request.SourceDocumentNumber,
            MovementDate = request.MovementDate.ToDateTime(TimeOnly.MinValue), AccountingDate = request.AccountingDate.ToDateTime(TimeOnly.MinValue),
            request.PartnerId, EmployeeId = employeeId, request.IsExternalFact, Lines = lines.AsTableValuedParameter("inv.tt_StockMovementRequest"),
            UserId = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct))).ConfigureAwait(false);
        return Outcome(rows.ToList());
    }

    public async Task<StockPostingOutcome> ReverseAsync(StockReversalRequest request, int userId, CancellationToken ct)
    {
        var ids = new DataTable();
        ids.Columns.Add("Id", typeof(int));
        foreach (var id in request.SourceLineIds ?? [])
        {
            ids.Rows.Add(checked((int)id));
        }

        var rows = await Translated(() => session.Connection.QueryAsync<Row>(new CommandDefinition("inv.usp_StockMovement_Reverse", new
        {
            request.CompanyId, request.SourceModule, request.SourceDocumentId, SourceLineIds = ids.AsTableValuedParameter("inv.IdList"),
            MovementDate = request.MovementDate.ToDateTime(TimeOnly.MinValue), request.ReversalDocumentType, request.ReversalDocumentId,
            request.ReversalDocumentNumber, UserId = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct))).ConfigureAwait(false);
        return Outcome(rows.ToList());
    }

    public async Task<IReadOnlyList<StockAvailabilityDto>> GetAvailabilityAsync(int companyId, int? depotId, int? warehouseId,
        IReadOnlyCollection<int> itemVariantIds, DateOnly onDate, CancellationToken ct)
    {
        var ids = new DataTable();
        ids.Columns.Add("Id", typeof(int));
        foreach (var id in itemVariantIds.Distinct())
        {
            ids.Rows.Add(id);
        }

        return (await session.Connection.QueryAsync<StockAvailabilityDto>(new CommandDefinition("inv.usp_Stock_Availability", new
        {
            CompanyId = companyId, DepotId = depotId, WarehouseId = warehouseId, Variants = ids.AsTableValuedParameter("inv.IdList"),
            OnDate = onDate.ToDateTime(TimeOnly.MinValue),
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false)).ToList();
    }

    private static StockPostingOutcome Outcome(IReadOnlyList<Row> rows) =>
        new(rows.Select(r => new PostedStockMovement(r.MovementId, r.SourceLineId, r.SourceRole, r.WarehouseId, r.DepotId, r.ItemVariantId, r.QtyBase,
            r.UnitCost, r.CostAmount, r.IsForcedNegative, r.MovementTypeCode, r.ReasonCode)).ToList(), rows.Count > 0 && rows.All(r => r.Replayed));

    private static DataTable Requests()
    {
        var t = new DataTable();
        t.Columns.Add("RowNo", typeof(int));
        t.Columns.Add("SourceLineId", typeof(long));
        t.Columns.Add("SourceRole", typeof(byte));
        t.Columns.Add("MovementTypeCode", typeof(string));
        t.Columns.Add("DepotId", typeof(int));
        t.Columns.Add("WarehouseId", typeof(int));
        t.Columns.Add("ItemVariantId", typeof(int));
        t.Columns.Add("QtyBase", typeof(decimal));
        t.Columns.Add("InboundUnitCost", typeof(decimal));
        t.Columns.Add("LotId", typeof(int));
        t.Columns.Add("SerialId", typeof(int));
        t.Columns.Add("CostCenterId", typeof(int));
        t.Columns.Add("ProjectId", typeof(int));
        t.Columns.Add("ReasonId", typeof(int));
        t.Columns.Add("CounterRowNo", typeof(int));
        t.Columns.Add("ReversalOfId", typeof(long));
        t.Columns.Add("FixedCostAmount", typeof(decimal));
        return t;
    }

    private sealed record Row(long MovementId, long SourceLineId, byte SourceRole, int WarehouseId, int? DepotId, int ItemVariantId, decimal QtyBase,
        decimal UnitCost, decimal CostAmount, bool IsForcedNegative, string MovementTypeCode, string? ReasonCode, bool Replayed);
}
