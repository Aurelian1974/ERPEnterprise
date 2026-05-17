using Finance.Application.Abstractions;
using Finance.Domain.Aggregates;
using Finance.Domain.Errors;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Finance.Application.Features.Invoices.Create;

public sealed class CreateInvoiceCommandHandler : ICommandHandler<CreateInvoiceCommand, Guid>
{
    private readonly IInvoiceRepository _writeRepo;

    public CreateInvoiceCommandHandler(IInvoiceRepository writeRepo)
    {
        _writeRepo = writeRepo;
    }

    public async Task<Result<Guid>> Handle(CreateInvoiceCommand cmd, CancellationToken ct)
    {
        if (cmd.Lines.Count == 0)
            return Result<Guid>.Failure(FinanceErrors.Invoices.NoLines);

        var lines = cmd.Lines.Select(l => (l.Description, l.Quantity, l.UnitPrice, l.VatRate));

        Invoice invoice = Invoice.Create(
            cmd.TenantId,
            cmd.CustomerId,
            cmd.Currency,
            cmd.DueDate,
            lines);

        await _writeRepo.InsertAsync(invoice, ct);

        return Result<Guid>.Success(invoice.Id);
    }
}
