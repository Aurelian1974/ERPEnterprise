using Finance.Application.Features.Invoices.GetById;

namespace Finance.Application.Abstractions;

public interface IInvoiceReadRepository
{
    Task<InvoiceDetailDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<InvoiceListDto>> ListAsync(InvoiceFilters filters, Guid tenantId, CancellationToken ct = default);
}
