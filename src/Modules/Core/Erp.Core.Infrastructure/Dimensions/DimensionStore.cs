using System.Data;
using Dapper;
using Erp.Core.Application.Abstractions;
using Erp.Core.Application.Dimensions;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Application.Users;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Dimensions;

/// <summary>Dimensiunile prin procedurile core.usp_Dimension* (NET-06).</summary>
public sealed class DimensionStore(IDbSession session) : IDimensionStore
{
    public async Task<DimensionPageDto> ListAsync(DimensionSearchCriteria criteria, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("core.usp_Dimension_List", new
        {
            criteria.UserId, DimensionType = Type(criteria.DimensionType), criteria.CompanyId, criteria.Search, criteria.IncludeInactive,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var types = (await grid.ReadAsync<DimensionTypeDto>().ConfigureAwait(false)).ToList();
        var values = (await grid.ReadAsync<ValueRow>().ConfigureAwait(false)).Select(v => new DimensionValueItemDto(v.Id, v.CompanyId, v.CompanyName,
            v.DimensionType, v.Code, v.Name, v.ParentId, v.ParentCode, v.ParentName, v.ResponsibleUserId, v.ResponsibleName, Date(v.ValidFrom),
            Date(v.ValidTo), v.IsActive, v.ActiveChildren, v.RowVersion)).ToList();
        var companies = (await grid.ReadAsync<CompanyOptionDto>().ConfigureAwait(false)).ToList();
        var users = (await grid.ReadAsync<UserOptionDto>().ConfigureAwait(false)).ToList();
        return new DimensionPageDto(types, values, companies, users);
    }

    public Task<SavedEntity> SaveAsync(DimensionValueRecord value, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(new CommandDefinition("core.usp_DimensionValue_Save", new
        {
            value.Id, value.CompanyId, DimensionType = Type(value.DimensionType), value.Code, value.Name, value.ParentId, value.ResponsibleUserId,
            ValidFrom = DateTime(value.ValidFrom), ValidTo = DateTime(value.ValidTo), value.IsActive, value.RowVersion, UserId = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    internal static DbString Type(string type) => new() { Value = type, Length = 20, IsAnsi = true };

    internal static DateOnly? Date(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;

    private static DateTime? DateTime(DateOnly? value) => value?.ToDateTime(TimeOnly.MinValue);

    private sealed record ValueRow(int Id, int CompanyId, string CompanyName, string DimensionType, string Code, string Name, int? ParentId,
        string? ParentCode, string? ParentName, int? ResponsibleUserId, string? ResponsibleName, DateTime? ValidFrom, DateTime? ValidTo, bool IsActive,
        int ActiveChildren, byte[] RowVersion);
}

/// <summary>IDimensionLookup (02.01, 6): citirea si crearea dupa cod, cu codurile THROW traduse si din afara comenzilor.</summary>
public sealed class DimensionLookup(IDbSession session, ICurrentContext context, ISqlErrorTranslator errors) : IDimensionLookup
{
    public async Task<DimensionValueDto?> GetAsync(int dimensionValueId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("core.usp_DimensionValue_Get",
            new { Id = dimensionValueId }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        return row is null ? null : new DimensionValueDto(row.Id, row.CompanyId, row.DimensionType, row.Code, row.Name, row.ParentId,
            DimensionStore.Date(row.ValidFrom), DimensionStore.Date(row.ValidTo), row.IsActive);
    }

    public async Task<int> EnsureAsync(int companyId, string dimensionType, string code, string name, CancellationToken ct)
    {
        try
        {
            return await session.Connection.QuerySingleAsync<int>(new CommandDefinition("core.usp_DimensionValue_Ensure", new
            {
                CompanyId = companyId, DimensionType = DimensionStore.Type(dimensionType), Code = code, Name = name, context.UserId,
            }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (Exception exception) when (errors.Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    public async Task SyncAsync(int dimensionValueId, string code, string name, bool isActive, CancellationToken ct)
    {
        try
        {
            await session.Connection.ExecuteAsync(new CommandDefinition("core.usp_DimensionValue_Sync", new
            {
                Id = dimensionValueId, Code = code, Name = name, IsActive = isActive, context.UserId,
            }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (Exception exception) when (errors.Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    private sealed record Row(int Id, int CompanyId, string DimensionType, string Code, string Name, int? ParentId, DateTime? ValidFrom,
        DateTime? ValidTo, bool IsActive);
}
