using Administration.Domain.Partners;

namespace Administration.Application.Abstractions;

public interface IPartnerRepository
{
    Task<Partner?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task InsertAsync(Partner partner, CancellationToken ct = default);
    Task UpdateAsync(Partner partner, CancellationToken ct = default);
    Task UpdateAnafAsync(Partner partner, AnafAdresaSediuSocial? sediuSocial, CancellationToken ct = default);
}
