using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application.Lots;
using Erp.Inventory.Domain;
using Microsoft.Data.SqlClient;

namespace Erp.Inventory.Infrastructure.Lots;

/// <summary>Seriile prin inv.usp_Serial_*, inv.usp_SerialIdentifier_Edit, inv.usp_ItemSerialIdentifierRule_Save (NET-06).</summary>
public sealed class SerialStore(IDbSession session) : ISerialStore
{
    private static readonly InventorySqlErrors Errors = new();

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DateOnly? Date(DateTime? value) => value is { } d ? DateOnly.FromDateTime(d) : null;

    public async Task<IReadOnlyList<SerialInfoDto>> FindAsync(int? companyId, int? itemVariantId, string value, string? typeCode, bool exact, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(int SerialId, string SerialNo, int ItemVariantId, int CompanyId, byte Status, int? WarehouseId, DateTime? WarrantyUntil)>(
            Command("inv.usp_Serial_Find", new { CompanyId = companyId, ItemVariantId = itemVariantId, Value = value, TypeCode = typeCode, Exact = exact }, ct))
            .ConfigureAwait(false))
        .Select(s => new SerialInfoDto(s.SerialId, s.SerialNo, s.ItemVariantId, s.CompanyId, (SerialStatus)s.Status, s.WarehouseId, Date(s.WarrantyUntil)))
        .ToList();

    public async Task<IReadOnlyList<SerialItemDto>> ListAsync(int companyId, int? itemVariantId, int? warehouseId, byte? status, string? search, CancellationToken ct) =>
        (await session.Connection.QueryAsync<SerialRow>(Command("inv.usp_Serial_List", new
        {
            CompanyId = companyId, ItemVariantId = itemVariantId, WarehouseId = warehouseId, Status = status, Search = search,
        }, ct)).ConfigureAwait(false)).Select(ToDto).ToList();

    public async Task<SerialDto?> GetAsync(int companyId, int serialId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_Serial_Get", new { CompanyId = companyId, SerialId = serialId }, ct))
            .ConfigureAwait(false);
        var serial = await grid.ReadSingleOrDefaultAsync<SerialRow>().ConfigureAwait(false);
        if (serial is null)
        {
            return null;
        }

        var identifiers = (await grid.ReadAsync<SerialIdentifierDto>().ConfigureAwait(false)).ToList();
        var movements = (await grid.ReadAsync<(long MovementId, DateTime MovementDate, string MovementTypeCode, string WarehouseCode, decimal QtyBase,
                string SourceDocumentType, string? SourceDocumentNumber, int? PartnerId)>().ConfigureAwait(false))
            .Select(m => new SerialMovementDto(m.MovementId, DateOnly.FromDateTime(m.MovementDate), m.MovementTypeCode, m.WarehouseCode, Quantity.Format(m.QtyBase),
                m.SourceDocumentType, m.SourceDocumentNumber, m.PartnerId)).ToList();
        return new SerialDto(ToDto(serial), identifiers, movements);
    }

    public async Task EditIdentifierAsync(int companyId, int serialId, string typeCode, string? value, string reason, int userId, CancellationToken ct)
    {
        try
        {
            await session.Connection.ExecuteAsync(Command("inv.usp_SerialIdentifier_Edit", new
            {
                CompanyId = companyId, SerialId = serialId, TypeCode = typeCode, Value = value, Reason = reason, UserId = userId,
            }, ct)).ConfigureAwait(false);
        }
        catch (SqlException e) when (Errors.Errors.TryGetValue(e.Number, out var error))
        {
            throw new ErpException(error.Kind, error.Code, e.Message, e);
        }
    }

    public async Task<IReadOnlyList<SerialIdentifierRuleDto>> SaveRuleAsync(int? itemId, int? itemGroupId, string typeCode, int sortOrder, bool requiredAtReceipt,
        bool requiredAtIssue, bool isUnique, bool onDocuments, bool delete, int userId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<SerialIdentifierRuleDto>(Command("inv.usp_ItemSerialIdentifierRule_Save", new
        {
            ItemId = itemId, ItemGroupId = itemGroupId, TypeCode = typeCode, SortOrder = sortOrder, RequiredAtReceipt = requiredAtReceipt,
            RequiredAtIssue = requiredAtIssue, IsUnique = isUnique, OnDocuments = onDocuments, Delete = delete, UserId = userId,
        }, ct)).ConfigureAwait(false)).ToList();

    public async Task<IReadOnlyList<(string Code, string? Mask, byte CheckKind)>> TypesAsync(CancellationToken ct) =>
        (await session.Connection.QueryAsync<(int Id, string Code, string Name, string? Mask, byte CheckKind, bool IsSystem, bool IsActive)>(
            Command("inv.usp_SerialIdentifierType_List", new { }, ct)).ConfigureAwait(false))
        .Where(t => t.IsActive).Select(t => (t.Code, t.Mask, t.CheckKind)).ToList();

    private static SerialItemDto ToDto(SerialRow r) => new(r.SerialId, r.SerialNo, r.ItemVariantId, r.VariantCode, r.VariantName, (SerialStatus)r.Status,
        r.WarehouseId, r.WarehouseCode, r.LotId, r.LotCode, Date(r.SoldAt), r.SoldToPartnerId, r.SoldToName, Date(r.WarrantyUntil), r.IsCapturedAtIssue);

    private sealed record SerialRow(int SerialId, string SerialNo, int ItemVariantId, string VariantCode, string VariantName, byte Status, int? WarehouseId,
        string? WarehouseCode, int? LotId, string? LotCode, DateTime? SoldAt, int? SoldToPartnerId, string? SoldToName, DateTime? WarrantyUntil,
        bool IsCapturedAtIssue);
}
