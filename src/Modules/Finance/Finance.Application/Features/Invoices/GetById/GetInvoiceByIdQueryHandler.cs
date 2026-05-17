using Finance.Application.Abstractions;
using Finance.Domain.Errors;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Finance.Application.Features.Invoices.GetById;

public sealed class GetInvoiceByIdQueryHandler : IQueryHandler<GetInvoiceByIdQuery, InvoiceDetailDto>
{
    private readonly IInvoiceReadRepository _repo;

    public GetInvoiceByIdQueryHandler(IInvoiceReadRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result<InvoiceDetailDto>> Handle(GetInvoiceByIdQuery query, CancellationToken ct)
    {
        InvoiceDetailDto? invoice = await _repo.GetByIdAsync(query.Id, query.TenantId, ct);

        if (invoice is null)
            return Result<InvoiceDetailDto>.Failure(FinanceErrors.Invoices.NotFound);

        return Result<InvoiceDetailDto>.Success(invoice);
    }
}
