using Finance.Application.Abstractions;
using Finance.Application.Features.Invoices.GetById;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Finance.Application.Features.Invoices.List;

public sealed class ListInvoicesQueryHandler : IQueryHandler<ListInvoicesQuery, IReadOnlyList<InvoiceListDto>>
{
    private readonly IInvoiceReadRepository _repo;

    public ListInvoicesQueryHandler(IInvoiceReadRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result<IReadOnlyList<InvoiceListDto>>> Handle(ListInvoicesQuery query, CancellationToken ct)
    {
        IReadOnlyList<InvoiceListDto> invoices = await _repo.ListAsync(query.Filters, query.TenantId, ct);
        return Result<IReadOnlyList<InvoiceListDto>>.Success(invoices);
    }
}
