using System.Data;
using System.Globalization;
using Dapper;
using Erp.Core.Application.Items;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Items;

/// <summary>Articolele si nomenclatoarele lor prin procedurile core.usp_* (NET-06), pe conexiunea si tranzactia cererii.</summary>
public sealed class ItemStore(IDbSession session) : IItemStore
{
    public async Task<ItemLookupsDto> GetLookupsAsync(int companyId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_ItemLookups_Get", new { CompanyId = companyId }, ct)).ConfigureAwait(false);
        var vat = (await grid.ReadAsync<VatRow>().ConfigureAwait(false)).Select(v => new VatRateDto(v.Id, v.Code, v.Name, Decimals.Format(v.Percent, 2),
            v.Category, DateOnly.FromDateTime(v.ValidFrom), v.ValidTo is { } to ? DateOnly.FromDateTime(to) : null, v.UsageCount, v.RowVersion)).ToList();
        var units = (await grid.ReadAsync<UnitOfMeasureDto>().ConfigureAwait(false)).ToList();
        var sets = (await grid.ReadAsync<SetRow>().ConfigureAwait(false)).ToList();
        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false)).ToLookup(l => l.UomSetId);
        var groups = (await grid.ReadAsync<ItemGroupDto>().ConfigureAwait(false)).ToList();
        var properties = (await grid.ReadAsync<PropertyDefinitionDto>().ConfigureAwait(false)).ToList();
        var options = (await grid.ReadAsync<PropertyOptionDto>().ConfigureAwait(false)).ToList();
        var users = (await grid.ReadAsync<UserOptionDto>().ConfigureAwait(false)).ToList();
        return new ItemLookupsDto(vat, units,
            [.. sets.Select(s => new UomSetDto(s.Id, s.Code, s.Name, s.IsActive, s.UsageCount, s.RowVersion, [.. lines[s.Id].Select(l => l.ToDto())]))],
            groups, properties, options, users);
    }

    public Task<int> SaveVatRatesAsync(IReadOnlyList<VatRateInput> rows, int userId, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Percent", typeof(decimal));
        table.Columns.Add("Category", typeof(string));
        table.Columns.Add("ValidFrom", typeof(DateTime));
        table.Columns.Add("ValidTo", typeof(DateTime));
        table.Columns.Add("RowVersion", typeof(byte[]));
        foreach (var r in rows)
        {
            table.Rows.Add((object?)r.Id ?? DBNull.Value, r.Code, r.Name, Parse(r.Percent), r.Category, r.ValidFrom.ToDateTime(TimeOnly.MinValue),
                r.ValidTo is { } to ? to.ToDateTime(TimeOnly.MinValue) : DBNull.Value, (object?)r.RowVersion ?? DBNull.Value);
        }

        return Batch("core.usp_VatRate_SaveBatch", table, "core.VatRateList", userId, ct);
    }

    public Task<int> SaveUnitsAsync(IReadOnlyList<UnitOfMeasureInput> rows, int userId, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("UneceCode", typeof(string));
        table.Columns.Add("IsActive", typeof(bool));
        table.Columns.Add("RowVersion", typeof(byte[]));
        foreach (var r in rows)
        {
            table.Rows.Add((object?)r.Id ?? DBNull.Value, r.Code, r.Name, r.UneceCode, r.IsActive, (object?)r.RowVersion ?? DBNull.Value);
        }

        return Batch("core.usp_UnitOfMeasure_SaveBatch", table, "core.UnitOfMeasureList", userId, ct);
    }

    public Task<SavedEntity> SaveUomSetAsync(SaveUomSet command, int userId, CancellationToken ct)
    {
        var parameters = new DynamicParameters(new { command.Id, command.Code, command.Name, command.IsActive, command.RowVersion, UserId = userId });
        parameters.Add("Lines", LineTable(command.Lines).AsTableValuedParameter("core.UomLineList"));
        return session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_UomSet_Save", parameters, ct));
    }

    public Task<SavedEntity> SaveItemGroupAsync(SaveItemGroup command, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_ItemGroup_Save", new
        {
            command.Id, command.Code, command.Name, command.ParentId, command.SortOrder, command.IsActive, command.RowVersion, UserId = userId,
        }, ct));

    public async Task<ItemPageDto> SearchAsync(ItemSearchCriteria criteria, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_Item_Search", SearchParameters(criteria, export: false), ct)).ConfigureAwait(false);
        var rows = (await grid.ReadAsync<SearchRow>().ConfigureAwait(false)).ToList();
        var active = await grid.ReadSingleAsync<int>().ConfigureAwait(false);
        var total = rows.Count > 0 ? rows[0].TotalCount : 0;
        if (rows.Count == 0 && criteria.Offset > 0)
        {
            total = (await SearchAsync(criteria with { Offset = 0, PageSize = 1 }, ct).ConfigureAwait(false)).Total;
        }

        return new ItemPageDto([.. rows.Select(r => new ItemListItemDto(r.Id, r.Code, r.Name, r.ItemType, r.IsStockable, r.IsActive, r.VatRateCode,
            Decimals.Format(r.VatPercent, 2), r.BaseUomCode, r.GroupName, r.Barcode, OwnerRef.LevelOf(r.OwnerCompanyId, r.OwnerBranchId, r.OwnerLocationId).Level,
            r.OwnerName))], total, active);
    }

    public async Task<IReadOnlyList<ItemExportRow>> ExportAsync(ItemSearchCriteria criteria, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("core.usp_Item_Search", SearchParameters(criteria, export: true),
            session.Transaction, commandType: CommandType.StoredProcedure, commandTimeout: 300, cancellationToken: ct)).ConfigureAwait(false);
        var rows = (await grid.ReadAsync<SearchRow>().ConfigureAwait(false)).ToList();
        return [.. rows.Select(r => new ItemExportRow(r.Code, r.Name, r.ItemType, r.IsStockable, r.IsActive, r.VatRateCode, r.BaseUomCode, r.UomSetCode,
            r.GroupCode, r.Barcode, r.PricePrecision, r.QuantityPrecision, r.CnCode, r.DepositItemCode, r.NetWeightKg, r.GrossWeightKg, r.VolumeM3))];
    }

    public async Task<ItemDetailDto?> GetAsync(int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_Item_Get", new { Id = id }, ct)).ConfigureAwait(false);
        var row = await grid.ReadSingleOrDefaultAsync<ItemRow>().ConfigureAwait(false);
        var uoms = (await grid.ReadAsync<LineRow2>().ConfigureAwait(false)).Select(l => new UomLineDto(l.UomId, Decimals.Trim(l.FactorToBase), l.IsBase,
            l.IsDefaultSales, l.IsDefaultPurchase, l.IsDefaultPos)).ToList();
        var barcodes = (await grid.ReadAsync<ItemBarcodeDto>().ConfigureAwait(false)).ToList();
        var properties = (await grid.ReadAsync<ValueRow>().ConfigureAwait(false))
            .Select(v => new PartnerPropertyValue(v.PropertyDefinitionId, v.ValueText, v.ValueNumber?.ToString("0.######", CultureInfo.InvariantCulture),
                v.ValueDate is { } date ? DateOnly.FromDateTime(date) : null, v.ValueBool, v.ValueOptionId, v.ValueUserId))
            .ToList();
        return row is null ? null : new ItemDetailDto(row.Id, row.Code, row.Name, row.ItemType, row.UomSetId, row.BaseUomId, row.DefaultVatRateId, row.ItemGroupId,
            row.PricePrecision, row.QuantityPrecision, row.IsStockable, row.CnCode, row.DepositItemId, row.DepositItemCode, row.DepositItemName,
            Measure(row.NetWeightKg), Measure(row.GrossWeightKg), Measure(row.VolumeM3), row.IsActive, DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc),
            row.ModifiedByName, DateTime.SpecifyKind(row.LastChangedAt, DateTimeKind.Utc), row.RowVersion, uoms, barcodes, properties,
            OwnerRef.LevelOf(row.OwnerCompanyId, row.OwnerBranchId, row.OwnerLocationId).Level,
            OwnerRef.LevelOf(row.OwnerCompanyId, row.OwnerBranchId, row.OwnerLocationId).NodeId, row.OwnerName);
    }

    public Task<SavedEntity> SaveAsync(ItemRecord item, int userId, CancellationToken ct)
    {
        var parameters = new DynamicParameters(new
        {
            item.Id, item.Code, item.Name, item.ItemType, item.UomSetId, item.DefaultVatRateId, item.ItemGroupId, item.PricePrecision, item.QuantityPrecision,
            item.IsStockable, item.CnCode, item.DepositItemId, item.NetWeightKg, item.GrossWeightKg, item.VolumeM3, item.IsActive,
            SaveUoms = item.Uoms is not null, SaveProperties = item.Properties is not null, item.RowVersion, UserId = userId,
            SaveOwner = item.Owner is not null, OwnerCompanyId = item.Owner?.CompanyId, OwnerBranchId = item.Owner?.BranchId,
            OwnerLocationId = item.Owner?.LocationId,
        });
        parameters.Add("Uoms", LineTable(item.Uoms ?? []).AsTableValuedParameter("core.UomLineList"));
        parameters.Add("Properties", PropertyTable(item.Properties ?? []).AsTableValuedParameter("core.PropertyValueList"));
        return session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_Item_Save", parameters, ct));
    }

    public Task<SavedEntity> SaveBarcodeAsync(SaveItemBarcode command, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_ItemBarcode_Save", new
        {
            command.Id, command.ItemId, Barcode = new DbString { Value = command.Barcode, Length = 50, IsAnsi = true }, command.BarcodeType, command.UomId,
            command.IsActive, command.RowVersion, UserId = userId, command.VariantId,
        }, ct));

    public async Task<IReadOnlyList<PartnerHistoryEntryDto>> GetHistoryAsync(int id, CancellationToken ct) =>
        [.. (await session.Connection.QueryAsync<HistoryRow>(Command("core.usp_Item_History", new { Id = id }, ct)).ConfigureAwait(false))
            .Select(h => new PartnerHistoryEntryDto(h.Id, DateTime.SpecifyKind(h.OccurredAt, DateTimeKind.Utc), h.Action, h.EntityType, h.EntityId,
                h.UserName, h.Changes))];

    public Task<bool> EnsureCodeSeriesAsync(int companyId, short year, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<bool>(Command("core.usp_NumberSeries_EnsureDefault", new
        {
            CompanyId = companyId, DocumentTypeCode = ItemCodeSeries.DocumentType, Year = year, Prefix = ItemCodeSeries.DefaultPrefix,
            Padding = ItemCodeSeries.DefaultPadding, UserId = userId,
        }, ct));

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<bool>(Command("core.usp_Item_CodeExists", new { Code = code }, ct));

    public async Task<IReadOnlyList<ExistingItem>> FindForImportAsync(IReadOnlyList<string> codes, CancellationToken ct)
    {
        if (codes.Count == 0)
        {
            return [];
        }

        var table = new DataTable();
        table.Columns.Add("RowNumber", typeof(int));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("FiscalCode", typeof(string));
        for (var i = 0; i < codes.Count; i++)
        {
            table.Rows.Add(i, codes[i], DBNull.Value);
        }

        var parameters = new DynamicParameters();
        parameters.Add("Keys", table.AsTableValuedParameter("core.PartnerKeyList"));
        return [.. await session.Connection.QueryAsync<ExistingItem>(Command("core.usp_Item_FindForImport", parameters, ct)).ConfigureAwait(false)];
    }

    public async Task<IReadOnlyList<ExistingBarcode>> FindBarcodesAsync(IReadOnlyList<string> barcodes, CancellationToken ct)
    {
        if (barcodes.Count == 0)
        {
            return [];
        }

        var table = new DataTable();
        table.Columns.Add("RowNumber", typeof(int));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("FiscalCode", typeof(string));
        for (var i = 0; i < barcodes.Count; i++)
        {
            table.Rows.Add(i, barcodes[i], DBNull.Value);
        }

        var parameters = new DynamicParameters();
        parameters.Add("Keys", table.AsTableValuedParameter("core.PartnerKeyList"));
        return [.. await session.Connection.QueryAsync<ExistingBarcode>(Command("core.usp_ItemBarcode_Find", parameters, ct)).ConfigureAwait(false)];
    }

    private async Task<int> Batch(string procedure, DataTable rows, string typeName, int userId, CancellationToken ct)
    {
        var parameters = new DynamicParameters(new { UserId = userId });
        parameters.Add("Rows", rows.AsTableValuedParameter(typeName));
        return await session.Connection.QuerySingleAsync<int>(Command(procedure, parameters, ct)).ConfigureAwait(false);
    }

    private static DynamicParameters SearchParameters(ItemSearchCriteria c, bool export)
    {
        var parameters = new DynamicParameters(new
        {
            c.Search, c.IsStockable, c.ItemType, c.ItemGroupId, c.IsActive, c.PropertyDefinitionId, c.PropertyOptionId, c.PropertyBool, c.Offset,
            c.PageSize, Export = export, c.UserId, c.CompanyId, OwnerFilter = c.Owner, c.OwnerBranchId, c.OwnerLocationId, c.ContextBranchId,
            c.ContextLocationId,
        });
        parameters.Add("Barcode", c.Barcode, DbType.AnsiString, size: 50);
        return parameters;
    }

    private static DataTable LineTable(IReadOnlyList<UomLineDto> lines)
    {
        var table = new DataTable();
        table.Columns.Add("UomId", typeof(int));
        table.Columns.Add("FactorToBase", typeof(decimal));
        table.Columns.Add("IsBase", typeof(bool));
        table.Columns.Add("IsDefaultSales", typeof(bool));
        table.Columns.Add("IsDefaultPurchase", typeof(bool));
        table.Columns.Add("IsDefaultPos", typeof(bool));
        table.Columns.Add("SortOrder", typeof(int));
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            table.Rows.Add(l.UomId, Parse(l.FactorToBase), l.IsBase, l.IsDefaultSales, l.IsDefaultPurchase, l.IsDefaultPos, (i + 1) * 10);
        }

        return table;
    }

    private static DataTable PropertyTable(IReadOnlyList<PartnerPropertyValue> values)
    {
        var table = new DataTable();
        table.Columns.Add("PropertyDefinitionId", typeof(int));
        table.Columns.Add("ValueText", typeof(string));
        table.Columns.Add("ValueNumber", typeof(decimal));
        table.Columns.Add("ValueDate", typeof(DateTime));
        table.Columns.Add("ValueBool", typeof(bool));
        table.Columns.Add("ValueOptionId", typeof(int));
        table.Columns.Add("ValueUserId", typeof(int));
        foreach (var v in values)
        {
            table.Rows.Add(v.PropertyDefinitionId, (object?)v.ValueText?.Trim() ?? DBNull.Value,
                v.ValueNumber is null ? DBNull.Value : decimal.Parse(v.ValueNumber, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
                v.ValueDate is { } date ? date.ToDateTime(TimeOnly.MinValue) : DBNull.Value,
                (object?)v.ValueBool ?? DBNull.Value, (object?)v.ValueOptionId ?? DBNull.Value, (object?)v.ValueUserId ?? DBNull.Value);
        }

        return table;
    }

    private static decimal Parse(string value) => decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private static string? Measure(decimal? value) => value is { } v ? Decimals.Trim(v) : null;

    private CommandDefinition Command(string procedure, object? parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record VatRow(int Id, string Code, string Name, decimal Percent, string Category, DateTime ValidFrom, DateTime? ValidTo, int UsageCount,
        byte[] RowVersion);

    private sealed record SetRow(int Id, string Code, string Name, bool IsActive, int UsageCount, byte[] RowVersion);

    private sealed record LineRow(int UomSetId, int UomId, decimal FactorToBase, bool IsBase, bool IsDefaultSales, bool IsDefaultPurchase, bool IsDefaultPos,
        int SortOrder)
    {
        public UomLineDto ToDto() => new(UomId, Decimals.Trim(FactorToBase), IsBase, IsDefaultSales, IsDefaultPurchase, IsDefaultPos);
    }

    private sealed record LineRow2(int UomId, decimal FactorToBase, bool IsBase, bool IsDefaultSales, bool IsDefaultPurchase, bool IsDefaultPos, int SortOrder);

    private sealed record SearchRow(int Id, string Code, string Name, byte ItemType, bool IsStockable, bool IsActive, string VatRateCode, decimal VatPercent,
        string BaseUomCode, string? GroupCode, string? GroupName, string? Barcode, string UomSetCode, byte? PricePrecision, byte? QuantityPrecision,
        string? CnCode, string? DepositItemCode, decimal? NetWeightKg, decimal? GrossWeightKg, decimal? VolumeM3, int? OwnerCompanyId, int? OwnerBranchId,
        int? OwnerLocationId, string? OwnerName, int TotalCount);

    private sealed record ItemRow(int Id, string Code, string Name, byte ItemType, int UomSetId, int BaseUomId, int DefaultVatRateId, int? ItemGroupId,
        byte? PricePrecision, byte? QuantityPrecision, bool IsStockable, string? CnCode, int? DepositItemId, string? DepositItemCode, string? DepositItemName,
        decimal? NetWeightKg, decimal? GrossWeightKg, decimal? VolumeM3, bool IsActive, DateTime CreatedAt, string ModifiedByName, DateTime LastChangedAt,
        byte[] RowVersion, int? OwnerCompanyId, int? OwnerBranchId, int? OwnerLocationId, string? OwnerName);

    private sealed record ValueRow(int PropertyDefinitionId, string? ValueText, decimal? ValueNumber, DateTime? ValueDate, bool? ValueBool, int? ValueOptionId,
        int? ValueUserId);

    private sealed record HistoryRow(long Id, DateTime OccurredAt, string Action, string EntityType, long? EntityId, string UserName, string? Changes);
}
