using Shared.Kernel.Abstractions;

namespace Finance.Application.Features.Invoices.GetById;

public sealed record GetInvoiceByIdQuery(Guid Id, Guid TenantId) : IQuery<InvoiceDetailDto>;
