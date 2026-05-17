using Finance.Application.Abstractions;
using Finance.Domain.Aggregates;
using Finance.Domain.Errors;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Finance.Application.Features.Invoices.Approve;

public sealed class ApproveInvoiceCommandHandler : ICommandHandler<ApproveInvoiceCommand>
{
    private readonly IInvoiceRepository _repo;

    public ApproveInvoiceCommandHandler(IInvoiceRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result> Handle(ApproveInvoiceCommand cmd, CancellationToken ct)
    {
        Invoice? invoice = await _repo.GetByIdAsync(cmd.InvoiceId, cmd.TenantId, ct);

        if (invoice is null)
            return Result.Failure(FinanceErrors.Invoices.NotFound);

        Result result = invoice.Approve();

        if (result.IsFailure)
            return result;

        await _repo.UpdateAsync(invoice, ct);

        return Result.Success();
    }
}
