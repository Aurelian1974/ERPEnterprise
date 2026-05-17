using Dapper;
using Finance.Application.Abstractions;
using Finance.Domain.Aggregates;
using Finance.Domain.Enums;
using Shared.Kernel.Abstractions;
using System.Data;

namespace Finance.Infrastructure.Repositories;

public sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public InvoiceRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Invoice?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();

        // Read aggregate data via SP — the domain object is reconstructed from the read model
        var data = await conn.QuerySingleOrDefaultAsync<InvoiceData>(
            new CommandDefinition(
                "finance.usp_GetInvoiceById",
                new { Id = id, TenantId = tenantId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));

        if (data is null) return null;

        // Reconstruct via factory method using persisted state
        return InvoiceRehydrator.Rehydrate(data);
    }

    public async Task InsertAsync(Invoice invoice, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();
        using var tx = conn.BeginTransaction();

        await conn.ExecuteAsync(
            new CommandDefinition(
                "finance.usp_CreateInvoice",
                new
                {
                    Id = invoice.Id,
                    TenantId = invoice.TenantId,
                    CustomerId = invoice.CustomerId,
                    Currency = invoice.Currency,
                    DueDate = invoice.DueDate,
                    Status = invoice.Status.ToString(),
                    CreatedAtUtc = invoice.CreatedAtUtc
                },
                transaction: tx,
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));

        foreach (var line in invoice.Lines)
        {
            await conn.ExecuteAsync(
                new CommandDefinition(
                    "finance.usp_CreateInvoiceLine",
                    new
                    {
                        line.Id,
                        line.InvoiceId,
                        line.Description,
                        line.Quantity,
                        UnitPrice = line.UnitPrice.Amount,
                        line.VatRate
                    },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure,
                    cancellationToken: ct));
        }

        tx.Commit();
    }

    public async Task UpdateAsync(Invoice invoice, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();

        await conn.ExecuteAsync(
            new CommandDefinition(
                "finance.usp_UpdateInvoiceStatus",
                new
                {
                    Id = invoice.Id,
                    TenantId = invoice.TenantId,
                    Status = invoice.Status.ToString(),
                    invoice.ApprovedAtUtc,
                    invoice.PaidAtUtc
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
    }
}

internal sealed record InvoiceData(
    Guid Id,
    Guid TenantId,
    Guid CustomerId,
    string InvoiceNumber,
    string Currency,
    string Status,
    DateOnly DueDate,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? PaidAtUtc);

internal static class InvoiceRehydrator
{
    public static Invoice? Rehydrate(InvoiceData data) =>
        // Domain reconstruction not needed for write-side ops in CQRS
        // This is used only when we need the aggregate to call domain methods
        null; // TODO: implement full rehydration with lines if needed
}
