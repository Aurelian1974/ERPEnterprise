using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Warehouses;

namespace Erp.Inventory.Infrastructure.Warehouses;

/// <summary>Gestionarii si predarile prin inv.usp_WarehouseKeeper_* si inv.usp_WarehouseHandover_* (NET-06).</summary>
public sealed class WarehouseKeeperStore(IDbSession session) : IWarehouseKeeperStore
{
    public async Task<WarehouseKeepersDto> ListAsync(int companyId, int warehouseId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_WarehouseKeeper_List",
            new { CompanyId = companyId, WarehouseId = warehouseId }, ct)).ConfigureAwait(false);
        var keepers = (await grid.ReadAsync<KeeperRow>().ConfigureAwait(false)).Select(k => new WarehouseKeeperDto(k.Id, k.EmployeeId, k.EmployeeName, k.Role,
            DateOnly.FromDateTime(k.ValidFrom), Date(k.ValidTo), k.GuaranteeNote, k.HandoverId, Date(k.LeaveDate), k.RowVersion)).ToList();
        var handovers = (await grid.ReadAsync<HandoverRow>().ConfigureAwait(false)).Select(h => new WarehouseHandoverDto(h.Id,
            DateOnly.FromDateTime(h.HandoverDate), h.FromEmployeeId, h.FromEmployeeId is null ? null : h.FromEmployeeName, h.ToEmployeeId, h.ToEmployeeName,
            h.RequiresCount, h.InventoryCountId, h.Status, h.Notes, h.CompletedAt, h.RowVersion)).ToList();
        var employees = (await grid.ReadAsync<EmployeeRow>().ConfigureAwait(false)).Select(e => new KeeperEmployeeDto(e.Id, e.Name, Date(e.LeaveDate)))
            .ToList();
        return new WarehouseKeepersDto(keepers, handovers, employees);
    }

    public Task<InventorySaved> SaveKeeperAsync(int companyId, WarehouseKeeperRecord k, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_WarehouseKeeper_Save", new
        {
            CompanyId = companyId, k.Id, k.WarehouseId, k.EmployeeId, k.Role, ValidFrom = k.ValidFrom.ToDateTime(TimeOnly.MinValue),
            ValidTo = k.ValidTo?.ToDateTime(TimeOnly.MinValue), k.GuaranteeNote, k.RowVersion, UserId = userId,
        }, ct));

    public Task<InventorySaved> StartHandoverAsync(int companyId, int warehouseId, DateOnly handoverDate, int toEmployeeId, string? notes, int userId,
        CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_WarehouseHandover_Start", new
        {
            CompanyId = companyId, WarehouseId = warehouseId, HandoverDate = handoverDate.ToDateTime(TimeOnly.MinValue), ToEmployeeId = toEmployeeId,
            Notes = notes, UserId = userId,
        }, ct));

    public async Task<HandoverCompleted> CompleteHandoverAsync(int companyId, int id, byte[] rowVersion, int userId, CancellationToken ct)
    {
        var r = await session.Connection.QuerySingleAsync<CompletedRow>(Command("inv.usp_WarehouseHandover_Complete",
            new { CompanyId = companyId, Id = id, RowVersion = rowVersion, UserId = userId }, ct)).ConfigureAwait(false);
        return new HandoverCompleted(r.Id, r.RowVersion, r.WarehouseId, DateOnly.FromDateTime(r.HandoverDate), r.FromEmployeeId, r.ToEmployeeId,
            r.InventoryCountId);
    }

    public Task<InventorySaved> CancelHandoverAsync(int companyId, int id, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_WarehouseHandover_Cancel",
            new { CompanyId = companyId, Id = id, RowVersion = rowVersion, UserId = userId }, ct));

    private static DateOnly? Date(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record KeeperRow(int Id, int EmployeeId, string EmployeeName, byte Role, DateTime ValidFrom, DateTime? ValidTo, string? GuaranteeNote,
        int? HandoverId, DateTime? LeaveDate, byte[] RowVersion);

    private sealed record HandoverRow(int Id, DateTime HandoverDate, int? FromEmployeeId, string FromEmployeeName, int ToEmployeeId, string ToEmployeeName,
        bool RequiresCount, int? InventoryCountId, byte Status, string? Notes, DateTime? CompletedAt, byte[] RowVersion);

    private sealed record EmployeeRow(int Id, string Name, DateTime? LeaveDate);

    private sealed record CompletedRow(int Id, byte[] RowVersion, int WarehouseId, DateTime HandoverDate, int? FromEmployeeId, int ToEmployeeId,
        int? InventoryCountId);
}
