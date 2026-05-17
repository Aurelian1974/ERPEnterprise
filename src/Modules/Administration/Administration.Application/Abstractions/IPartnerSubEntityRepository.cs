namespace Administration.Application.Abstractions;

public interface IPartnerSubEntityRepository
{
    // Addresses
    Task UpsertAddressAsync(
        long? id, Guid partnerId, Guid tenantId,
        string addressType, string street, string city,
        string? county, string? postalCode, string country,
        bool isPrimary, CancellationToken ct = default);

    Task DeleteAddressAsync(long id, Guid partnerId, Guid tenantId, CancellationToken ct = default);

    // Contacts
    Task UpsertContactAsync(
        long? id, Guid partnerId, Guid tenantId,
        string fullName, string? position,
        string? phone, string? email,
        bool isPrimary, CancellationToken ct = default);

    Task DeleteContactAsync(long id, Guid partnerId, Guid tenantId, CancellationToken ct = default);

    // Bank Accounts
    Task UpsertBankAccountAsync(
        long? id, Guid partnerId, Guid tenantId,
        string iban, string bankName,
        string currency, bool isDefault,
        CancellationToken ct = default);

    Task DeleteBankAccountAsync(long id, Guid partnerId, Guid tenantId, CancellationToken ct = default);
}
