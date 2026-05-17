using Dapper;
using Finance.Application.Abstractions;
using Finance.Application.Features.Invoices.GetById;
using Shared.Kernel.Abstractions;
using System.Data;

namespace Finance.Infrastructure.Repositories;

public sealed class InvoiceReadRepository : IInvoiceReadRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public InvoiceReadRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<InvoiceDetailDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();

        var invoice = await conn.QuerySingleOrDefaultAsync<InvoiceDetailDto>(
            new CommandDefinition(
                "finance.usp_GetInvoiceById",
                new { Id = id, TenantId = tenantId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));

        return invoice;
    }

    public async Task<IReadOnlyList<InvoiceListDto>> ListAsync(
        Finance.Application.Features.Invoices.GetById.InvoiceFilters filters,
        Guid tenantId,
        CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();

        var results = await conn.QueryAsync<InvoiceListDto>(
            new CommandDefinition(
                "finance.usp_ListInvoicesPaged",
                new
                {
                    TenantId = tenantId,
                    Status = filters.Status?.ToString(),
                    CustomerId = filters.CustomerId,
                    DueDateFrom = filters.DueDateFrom,
                    DueDateTo = filters.DueDateTo,
                    Page = filters.Page,
                    PageSize = filters.PageSize
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));

        return results.ToList();
    }
}
