using Finance.Domain.Enums;

namespace Finance.Application.Features.Invoices.GetById;

public sealed record InvoiceDetailDto(
    Guid Id,
    Guid TenantId,
    Guid CustomerId,
    string InvoiceNumber,
    string Currency,
    string Status,
    DateOnly DueDate,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? PaidAtUtc,
    decimal TotalNet,
    decimal TotalVat,
    decimal TotalGross,
    IReadOnlyList<InvoiceLineDto> Lines);

public sealed record InvoiceLineDto(
    Guid Id,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount);
