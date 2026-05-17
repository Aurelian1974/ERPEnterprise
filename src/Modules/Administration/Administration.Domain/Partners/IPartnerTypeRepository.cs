namespace Administration.Domain.Partners;

public interface IPartnerTypeRepository
{
    Task<IReadOnlyList<PartnerType>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<PartnerType?> GetByIdAsync(byte id, CancellationToken ct = default);
    Task<byte> UpsertAsync(PartnerTypeUpsertData data, CancellationToken ct = default);
    Task DeleteAsync(byte id, CancellationToken ct = default);
}

public sealed record PartnerTypeUpsertData(
    byte? PartnerTypeId,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    bool AffectsIssuedInvoices,
    bool AffectsReceivedInvoices,
    short SortOrder,
    string UpdatedBy
);
