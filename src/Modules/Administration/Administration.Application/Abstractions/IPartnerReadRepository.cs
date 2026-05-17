using Administration.Application.Features.Partners;
using Shared.Kernel.Primitives;

namespace Administration.Application.Abstractions;

public interface IPartnerReadRepository
{
    Task<PagedResult<PartnerListItemDto>> ListAsync(
        Guid tenantId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<PartnerDetailDto?> GetByIdAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default);

    Task<bool> CodeExistsAsync(
        Guid tenantId,
        string code,
        Guid? excludeId,
        CancellationToken ct = default);

    Task<string> GetNextCodeAsync(Guid tenantId, CancellationToken ct = default);
}
