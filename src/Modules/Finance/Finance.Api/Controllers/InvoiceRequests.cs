namespace Finance.Api.Controllers;

public sealed record CreateInvoiceRequest(
    Guid CustomerId,
    string Currency,
    DateOnly DueDate,
    IReadOnlyList<CreateInvoiceLineRequest> Lines);

public sealed record CreateInvoiceLineRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate);
