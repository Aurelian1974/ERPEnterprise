using System.Data;
using Dapper;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Properties;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Data;

/// <summary>Definitiile proprietatilor prin core.usp_PropertyDefinition_* (NET-06).</summary>
public sealed class PropertyDefinitionStore(IDbSession session) : IPropertyDefinitionStore
{
    public async Task<IReadOnlyList<PropertyDefinitionAdminDto>> ListAsync(string entityType, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("core.usp_PropertyDefinition_List",
            new { EntityType = new DbString { Value = entityType, Length = 20, IsAnsi = true } }, session.Transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var rows = (await grid.ReadAsync<Row>().ConfigureAwait(false)).ToList();
        var options = (await grid.ReadAsync<PropertyOptionAdminDto>().ConfigureAwait(false)).ToLookup(o => o.PropertyDefinitionId);
        return [.. rows.Select(r => new PropertyDefinitionAdminDto(r.Id, r.EntityType, r.Code, r.Name, r.DataType, r.AppliesTo, r.Section, r.SortOrder,
            r.IsRequired, r.IsFilterable, r.CompanyId, r.IsActive, r.UsageCount, r.RowVersion, [.. options[r.Id]]))];
    }

    public Task<SavedEntity> SaveAsync(SavePropertyDefinition command, int userId, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("Label", typeof(string));
        table.Columns.Add("SortOrder", typeof(int));
        table.Columns.Add("IsActive", typeof(bool));
        foreach (var option in command.Options)
        {
            table.Rows.Add((object?)option.Id ?? DBNull.Value, option.Code, option.Label, option.SortOrder, option.IsActive);
        }

        var parameters = new DynamicParameters(new
        {
            command.Id, EntityType = new DbString { Value = command.EntityType, Length = 20, IsAnsi = true }, command.Code, command.Name, command.DataType,
            command.AppliesTo, command.Section, command.SortOrder, command.IsRequired, command.IsFilterable, command.CompanyId, command.IsActive,
            command.RowVersion, UserId = userId,
        });
        parameters.Add("Options", table.AsTableValuedParameter("core.PropertyOptionList"));
        return session.Connection.QuerySingleAsync<SavedEntity>(new CommandDefinition("core.usp_PropertyDefinition_Save", parameters, session.Transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    private sealed record Row(int Id, string EntityType, string Code, string Name, byte DataType, byte AppliesTo, string Section, int SortOrder,
        bool IsRequired, bool IsFilterable, int? CompanyId, bool IsActive, int UsageCount, byte[] RowVersion);
}
