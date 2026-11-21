using System.Data;
using Dapper;
using Erp.Core.Application.Items;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Items;

/// <summary>Atributele si variantele articolelor (core.usp_ItemAttribute_*, usp_ItemVariant_*; NET-06).</summary>
public sealed class ItemVariantStore(IDbSession session) : IItemVariantStore
{
    public async Task<ItemAttributePageDto> ListAttributesAsync(CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_ItemAttribute_List", null, ct)).ConfigureAwait(false);
        var attributes = (await grid.ReadAsync<ItemAttributeDto>().ConfigureAwait(false)).ToList();
        var values = (await grid.ReadAsync<ItemAttributeValueDto>().ConfigureAwait(false)).ToList();
        return new ItemAttributePageDto(attributes, values);
    }

    public Task<SavedEntity> SaveAttributeAsync(SaveItemAttribute command, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("SortOrder", typeof(int));
        table.Columns.Add("IsActive", typeof(bool));
        foreach (var v in command.Values)
        {
            table.Rows.Add((object?)v.Id ?? DBNull.Value, v.Code, v.Name, v.SortOrder, v.IsActive);
        }

        return session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_ItemAttribute_Save", new
        {
            command.Id, command.Code, command.Name, command.SortOrder, command.IsActive, command.RowVersion,
            Values = table.AsTableValuedParameter("core.ItemAttributeValueList"),
        }, ct));
    }

    public async Task<ItemVariantPageDto> ListVariantsAsync(int itemId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_ItemVariant_List", new { ItemId = itemId }, ct)).ConfigureAwait(false);
        var variants = (await grid.ReadAsync<ItemVariantItemDto>().ConfigureAwait(false)).ToList();
        var attributes = (await grid.ReadAsync<int>().ConfigureAwait(false)).ToList();
        return new ItemVariantPageDto(variants, attributes);
    }

    public Task<int> GenerateAsync(int itemId, IReadOnlyList<int> valueIds, int userId, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        foreach (var id in valueIds)
        {
            table.Rows.Add(id);
        }

        return session.Connection.QuerySingleAsync<int>(Command("core.usp_ItemVariant_Generate", new
        {
            ItemId = itemId, ValueIds = table.AsTableValuedParameter("core.IdList"), UserId = userId,
        }, ct));
    }

    public Task<SavedEntity> SaveVariantAsync(SaveItemVariant command, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_ItemVariant_Save", new
        {
            command.Id, command.ItemId, command.Code, command.Name, command.IsActive, command.RowVersion, UserId = userId,
        }, ct));

    private CommandDefinition Command(string procedure, object? parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);
}
