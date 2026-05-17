using Finance.Domain.Enums;

namespace Finance.Application.Features.Invoices.GetById;

public sealed record InvoiceFilters(
    InvoiceStatus? Status = null,
    Guid? CustomerId = null,
    DateOnly? DueDateFrom = null,
    DateOnly? DueDateTo = null,
    int Page = 1,
    int PageSize = 50);
