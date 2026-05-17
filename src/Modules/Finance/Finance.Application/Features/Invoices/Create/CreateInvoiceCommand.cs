using Shared.Kernel.Abstractions;

namespace Finance.Application.Features.Invoices.Create;

public sealed record CreateInvoiceCommand(
    Guid TenantId,
    Guid CustomerId,
    string Currency,
    DateOnly DueDate,
    IReadOnlyList<CreateInvoiceLineCommand> Lines) : ICommand<Guid>;

public sealed record CreateInvoiceLineCommand(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate);
