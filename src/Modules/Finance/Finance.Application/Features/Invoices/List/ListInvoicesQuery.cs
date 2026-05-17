using Finance.Application.Features.Invoices.GetById;
using Shared.Kernel.Abstractions;

namespace Finance.Application.Features.Invoices.List;

public sealed record ListInvoicesQuery(
    InvoiceFilters Filters,
    Guid TenantId) : IQuery<IReadOnlyList<InvoiceListDto>>;
