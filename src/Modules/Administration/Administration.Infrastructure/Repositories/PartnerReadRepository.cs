using Administration.Application.Abstractions;
using Administration.Application.Features.Partners;
using Dapper;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;
using System.Data;

namespace Administration.Infrastructure.Repositories;

public sealed class PartnerReadRepository : IPartnerReadRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PartnerReadRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagedResult<PartnerListItemDto>> ListAsync(
        Guid tenantId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();

        var rows = (await conn.QueryAsync<PartnerListItemDto>(
            new CommandDefinition(
                "administration.usp_ListPartners",
                new { TenantId = tenantId, Search = search, Page = page, PageSize = pageSize },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct))).ToList();

        var totalCount = rows.FirstOrDefault()?.TotalCount ?? 0;

        return new PagedResult<PartnerListItemDto>(rows, totalCount, page, pageSize);
    }

    public async Task<PartnerDetailDto?> GetByIdAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();

        using var multi = await conn.QueryMultipleAsync(
            new CommandDefinition(
                "administration.usp_GetPartnerById",
                new { Id = id, TenantId = tenantId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));

        var partner = await multi.ReadSingleOrDefaultAsync<PartnerRow>();
        if (partner is null) return null;

        var addresses    = (await multi.ReadAsync<PartnerAddressDto>()).ToList();
        var contacts     = (await multi.ReadAsync<PartnerContactDto>()).ToList();
        var bankAccounts = (await multi.ReadAsync<PartnerBankAccountDto>()).ToList();

        return new PartnerDetailDto(
            partner.Id, partner.TenantId, partner.Code, partner.Name,
            partner.Cui, partner.RegistrationNumber, partner.LegalForm,
            partner.PartnerTypeId, partner.PartnerTypeName,
            partner.IsVatPayer, partner.Phone, partner.Email,
            partner.IsActive, partner.Notes,
            partner.CreatedAt, partner.CreatedBy,
            partner.UpdatedAt, partner.UpdatedBy,
            partner.AnafVerifiedAt,
            addresses, contacts, bankAccounts);
    }

    public async Task<bool> CodeExistsAsync(
        Guid tenantId,
        string code,
        Guid? excludeId,
        CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();

        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                "administration.usp_CheckPartnerCodeExists",
                new { TenantId = tenantId, Code = code, ExcludeId = excludeId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
    }

    public async Task<string> GetNextCodeAsync(Guid tenantId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();

        return await conn.ExecuteScalarAsync<string>(
            new CommandDefinition(
                "administration.usp_GetNextPartnerCode",
                new { TenantId = tenantId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct)) ?? "PART00001";
    }

    private sealed record PartnerRow(
        Guid Id, Guid TenantId, string Code, string Name,
        string? Cui, string? RegistrationNumber, string? LegalForm,
        byte? PartnerTypeId, string? PartnerTypeName,
        bool IsVatPayer, string? Phone, string? Email,
        bool IsActive, string? Notes,
        DateTime CreatedAt, Guid CreatedBy,
        DateTime? UpdatedAt, Guid? UpdatedBy,
        DateTime? AnafVerifiedAt);
}
