using Administration.Domain.Partners;
using Dapper;
using Shared.Kernel.Abstractions;
using System.Data;

namespace Administration.Infrastructure.Repositories;

public sealed class PartnerTypeRepository(IDbConnectionFactory connectionFactory)
    : IPartnerTypeRepository
{
    public async Task<IReadOnlyList<PartnerType>> GetAllAsync(
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        using var conn = connectionFactory.Create();

        var rows = await conn.QueryAsync<PartnerType>(
            new CommandDefinition(
                "administration.usp_GetAllPartnerTypes",
                new { IncludeInactive = includeInactive },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<PartnerType?> GetByIdAsync(byte id, CancellationToken ct = default)
    {
        using var conn = connectionFactory.Create();

        return await conn.QuerySingleOrDefaultAsync<PartnerType>(
            new CommandDefinition(
                "administration.usp_GetPartnerTypeById",
                new { PartnerTypeId = id },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
    }

    public async Task<byte> UpsertAsync(PartnerTypeUpsertData data, CancellationToken ct = default)
    {
        using var conn = connectionFactory.Create();

        var parameters = new DynamicParameters();
        parameters.Add("@PartnerTypeId",           data.PartnerTypeId,           DbType.Byte,    direction: ParameterDirection.Input);
        parameters.Add("@Code",                    data.Code,                    DbType.String);
        parameters.Add("@Name",                    data.Name,                    DbType.String);
        parameters.Add("@Description",             data.Description,             DbType.String);
        parameters.Add("@IsActive",                data.IsActive,                DbType.Boolean);
        parameters.Add("@AffectsIssuedInvoices",   data.AffectsIssuedInvoices,   DbType.Boolean);
        parameters.Add("@AffectsReceivedInvoices", data.AffectsReceivedInvoices, DbType.Boolean);
        parameters.Add("@SortOrder",               data.SortOrder,               DbType.Int16);
        parameters.Add("@UpdatedBy",               data.UpdatedBy,               DbType.String);
        parameters.Add("@NewPartnerTypeId",        dbType: DbType.Byte,          direction: ParameterDirection.Output);

        await conn.ExecuteAsync(
            new CommandDefinition(
                "administration.usp_UpsertPartnerType",
                parameters,
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));

        return parameters.Get<byte>("@NewPartnerTypeId");
    }

    public async Task DeleteAsync(byte id, CancellationToken ct = default)
    {
        using var conn = connectionFactory.Create();

        await conn.ExecuteAsync(
            new CommandDefinition(
                "administration.usp_DeletePartnerType",
                new { PartnerTypeId = id },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
    }
}
