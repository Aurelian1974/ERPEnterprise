using Finance.Domain.Aggregates;
using Finance.Domain.Enums;

namespace Finance.Application.Abstractions;

public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task InsertAsync(Invoice invoice, CancellationToken ct = default);
    Task UpdateAsync(Invoice invoice, CancellationToken ct = default);
}
