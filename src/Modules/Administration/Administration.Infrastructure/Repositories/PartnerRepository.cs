using Administration.Application.Abstractions;
using Administration.Domain.Partners;
using Dapper;
using Shared.Kernel.Abstractions;
using System.Data;

namespace Administration.Infrastructure.Repositories;

public sealed class PartnerRepository : IPartnerRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PartnerRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Partner?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();

        var data = await conn.QuerySingleOrDefaultAsync<PartnerData>(
            new CommandDefinition(
                "administration.usp_GetPartnerDomainById",
                new { Id = id, TenantId = tenantId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));

        if (data is null) return null;

        return Partner.Rehydrate(
            data.Id, data.TenantId, data.Code, data.Name,
            data.Cui, data.RegistrationNumber, data.LegalForm,
            data.PartnerTypeId, data.IsVatPayer, data.Phone, data.Email,
            data.IsActive, data.Notes, data.CreatedAt, data.CreatedBy,
            data.UpdatedAt, data.UpdatedBy, data.AnafVerifiedAt);
    }

    public async Task InsertAsync(Partner partner, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();

        await conn.ExecuteAsync(
            new CommandDefinition(
                "administration.usp_CreatePartner",
                new
                {
                    partner.Id,
                    partner.TenantId,
                    partner.Code,
                    partner.Name,
                    partner.Cui,
                    partner.RegistrationNumber,
                    partner.LegalForm,
                    partner.PartnerTypeId,
                    partner.IsVatPayer,
                    partner.Phone,
                    partner.Email,
                    partner.Notes,
                    partner.AnafVerifiedAt,
                    partner.CreatedBy,
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
    }

    public async Task UpdateAsync(Partner partner, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();

        await conn.ExecuteAsync(
            new CommandDefinition(
                "administration.usp_UpdatePartner",
                new
                {
                    partner.Id,
                    partner.TenantId,
                    partner.Code,
                    partner.Name,
                    partner.Cui,
                    partner.RegistrationNumber,
                    partner.LegalForm,
                    partner.PartnerTypeId,
                    partner.IsVatPayer,
                    partner.Phone,
                    partner.Email,
                    partner.Notes,
                    partner.IsActive,
                    UpdatedBy = partner.UpdatedBy,
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
    }

    public async Task UpdateAnafAsync(Partner partner, AnafAdresaSediuSocial? sediuSocial, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();

        var street = sediuSocial is null
            ? null
            : $"{sediuSocial.Strada ?? string.Empty} {sediuSocial.Numar ?? string.Empty}".Trim();

        if (string.IsNullOrWhiteSpace(street))
            street = sediuSocial?.Localitate;

        await conn.ExecuteAsync(
            new CommandDefinition(
                "administration.usp_ApplyAnafData",
                new
                {
                    partner.Id,
                    partner.TenantId,
                    partner.IsVatPayer,
                    partner.RegistrationNumber,
                    partner.LegalForm,
                    partner.Phone,
                    AnafVerifiedAt         = partner.AnafVerifiedAt,
                    UpdatedBy              = partner.UpdatedBy,
                    SediuSocialStreet      = street,
                    SediuSocialCity        = sediuSocial?.Localitate,
                    SediuSocialCounty      = sediuSocial?.Judet,
                    SediuSocialPostalCode  = sediuSocial?.CodPostal,
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
    }

    private sealed record PartnerData(
        Guid Id, Guid TenantId, string Code, string Name,
        string? Cui, string? RegistrationNumber, string? LegalForm,
        byte? PartnerTypeId, bool IsVatPayer, string? Phone, string? Email,
        bool IsActive, string? Notes, DateTime CreatedAt, Guid CreatedBy,
        DateTime? UpdatedAt, Guid? UpdatedBy, DateTime? AnafVerifiedAt);
}
