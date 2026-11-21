using System.Data;
using Dapper;
using Erp.Core.Application.Items;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Items;

/// <summary>Codurile articolelor la parteneri si traducerile (core.usp_ItemReference_*, usp_ItemTranslation_*, usp_Item_GetName; NET-06).</summary>
public sealed class ItemCatalogStore(IDbSession session) : IItemCatalogStore
{
    public async Task<IReadOnlyList<ItemReferenceItemDto>> ListReferencesAsync(int itemId, CancellationToken ct) =>
        [.. await session.Connection.QueryAsync<ItemReferenceItemDto>(Command("core.usp_ItemReference_List", new { ItemId = itemId }, ct)).ConfigureAwait(false)];

    public Task<SavedEntity> SaveReferenceAsync(SaveItemReference command, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_ItemReference_Save", new
        {
            command.Id, command.ItemId, command.PartnerId, command.ReferenceType, command.Code, command.Description, command.UomId, command.IsActive,
            command.RowVersion, UserId = userId,
        }, ct));

    public async Task<IReadOnlyList<ItemTranslationDto>> ListTranslationsAsync(int itemId, CancellationToken ct) =>
        [.. await session.Connection.QueryAsync<ItemTranslationDto>(Command("core.usp_ItemTranslation_List", new { ItemId = itemId }, ct)).ConfigureAwait(false)];

    public Task SaveTranslationsAsync(int itemId, IReadOnlyList<ItemTranslationDto> translations, int userId, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("LanguageCode", typeof(string));
        table.Columns.Add("Name", typeof(string));
        foreach (var t in translations)
        {
            table.Rows.Add(t.LanguageCode, t.Name);
        }

        return session.Connection.ExecuteAsync(Command("core.usp_ItemTranslation_Save", new
        {
            ItemId = itemId, Translations = table.AsTableValuedParameter("core.ItemTranslationList"), UserId = userId,
        }, ct));
    }

    public Task<ItemReferenceDto?> FindReferenceAsync(int partnerId, byte referenceType, string? code, int? itemId, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<ItemReferenceDto>(Command("core.usp_ItemReference_Find", new
        {
            PartnerId = partnerId, ReferenceType = referenceType, Code = code, ItemId = itemId,
        }, ct));

    public Task<string?> GetNameAsync(int itemId, string? language, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<string?>(Command("core.usp_Item_GetName", new
        {
            ItemId = itemId, Language = new DbString { Value = language, Length = 2, IsAnsi = true, IsFixedLength = true },
        }, ct));

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);
}
