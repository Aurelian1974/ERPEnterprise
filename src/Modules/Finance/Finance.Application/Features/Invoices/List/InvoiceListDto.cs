using Finance.Domain.Enums;

namespace Finance.Application.Features.Invoices.GetById;

public sealed record InvoiceListDto(
    Guid Id,
    string InvoiceNumber,
    Guid CustomerId,
    string Currency,
    string Status,
    DateOnly DueDate,
    decimal TotalGross,
    DateTime CreatedAtUtc);
