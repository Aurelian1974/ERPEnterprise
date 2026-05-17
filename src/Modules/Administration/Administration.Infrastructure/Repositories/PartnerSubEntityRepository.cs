using Administration.Application.Abstractions;
using Dapper;
using Shared.Kernel.Abstractions;
using System.Data;

namespace Administration.Infrastructure.Repositories;

public sealed class PartnerSubEntityRepository : IPartnerSubEntityRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PartnerSubEntityRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task UpsertAddressAsync(
        long? id, Guid partnerId, Guid tenantId,
        string addressType, string street, string city,
        string? county, string? postalCode, string country,
        bool isPrimary, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();
        await conn.ExecuteAsync(new CommandDefinition(
            "administration.usp_UpsertPartnerAddress",
            new { Id = id, PartnerId = partnerId, TenantId = tenantId,
                  AddressType = addressType, Street = street, City = city,
                  County = county, PostalCode = postalCode, Country = country,
                  IsPrimary = isPrimary },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task DeleteAddressAsync(long id, Guid partnerId, Guid tenantId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();
        await conn.ExecuteAsync(new CommandDefinition(
            "administration.usp_DeletePartnerAddress",
            new { Id = id, PartnerId = partnerId, TenantId = tenantId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task UpsertContactAsync(
        long? id, Guid partnerId, Guid tenantId,
        string fullName, string? position,
        string? phone, string? email,
        bool isPrimary, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();
        await conn.ExecuteAsync(new CommandDefinition(
            "administration.usp_UpsertPartnerContact",
            new { Id = id, PartnerId = partnerId, TenantId = tenantId,
                  FullName = fullName, Position = position,
                  Phone = phone, Email = email, IsPrimary = isPrimary },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task DeleteContactAsync(long id, Guid partnerId, Guid tenantId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();
        await conn.ExecuteAsync(new CommandDefinition(
            "administration.usp_DeletePartnerContact",
            new { Id = id, PartnerId = partnerId, TenantId = tenantId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task UpsertBankAccountAsync(
        long? id, Guid partnerId, Guid tenantId,
        string iban, string bankName,
        string currency, bool isDefault,
        CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();
        await conn.ExecuteAsync(new CommandDefinition(
            "administration.usp_UpsertPartnerBankAccount",
            new { Id = id, PartnerId = partnerId, TenantId = tenantId,
                  Iban = iban, BankName = bankName,
                  Currency = currency, IsDefault = isDefault },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task DeleteBankAccountAsync(long id, Guid partnerId, Guid tenantId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();
        await conn.ExecuteAsync(new CommandDefinition(
            "administration.usp_DeletePartnerBankAccount",
            new { Id = id, PartnerId = partnerId, TenantId = tenantId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }
}
