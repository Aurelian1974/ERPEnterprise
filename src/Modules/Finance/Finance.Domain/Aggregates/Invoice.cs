using Finance.Domain.DomainEvents;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Errors;
using Finance.Domain.ValueObjects;
using Shared.Kernel.Primitives;
using UUIDNext;

namespace Finance.Domain.Aggregates;

public sealed class Invoice : AggregateRoot
{
    private readonly List<InvoiceLine> _lines = [];

    private Invoice(
        Guid id,
        Guid tenantId,
        Guid customerId,
        string currency,
        DateOnly dueDate) : base(id)
    {
        TenantId = tenantId;
        CustomerId = customerId;
        Currency = currency;
        DueDate = dueDate;
        Status = InvoiceStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string InvoiceNumber { get; private set; } = string.Empty;
    public string Currency { get; private set; }
    public DateOnly DueDate { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public IReadOnlyList<InvoiceLine> Lines => _lines.AsReadOnly();

    public Money TotalNet => _lines
        .Aggregate(Money.Of(0, Currency), (acc, l) => acc.Add(l.NetAmount));

    public Money TotalVat => _lines
        .Aggregate(Money.Of(0, Currency), (acc, l) => acc.Add(l.VatAmount));

    public Money TotalGross => _lines
        .Aggregate(Money.Of(0, Currency), (acc, l) => acc.Add(l.GrossAmount));

    public static Invoice Create(
        Guid tenantId,
        Guid customerId,
        string currency,
        DateOnly dueDate,
        IEnumerable<(string Description, decimal Quantity, decimal UnitPrice, decimal VatRate)> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        var invoice = new Invoice(
            Uuid.NewDatabaseFriendly(Database.SqlServer),
            tenantId,
            customerId,
            currency.ToUpperInvariant(),
            dueDate);

        foreach (var (description, quantity, unitPrice, vatRate) in lines)
        {
            invoice._lines.Add(InvoiceLine.Create(
                invoice.Id,
                description,
                quantity,
                Money.Of(unitPrice, currency),
                vatRate));
        }

        if (invoice._lines.Count == 0)
            throw new InvalidOperationException(FinanceErrors.Invoices.NoLines.Description);

        invoice.RaiseDomainEvent(new InvoiceCreatedDomainEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            invoice.Id,
            tenantId,
            customerId));

        return invoice;
    }

    public Result Approve()
    {
        if (Status != InvoiceStatus.Submitted)
            return Result.Failure(FinanceErrors.Invoices.CannotApproveNonSubmitted);

        Status = InvoiceStatus.Approved;
        ApprovedAtUtc = DateTime.UtcNow;

        RaiseDomainEvent(new InvoiceApprovedDomainEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            Id,
            TenantId));

        return Result.Success();
    }

    public Result Submit()
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(new Shared.Kernel.Errors.Error(
                "Finance.Invoice.CannotSubmit",
                "Only draft invoices can be submitted."));

        Status = InvoiceStatus.Submitted;
        return Result.Success();
    }

    public Result MarkAsPaid()
    {
        if (Status == InvoiceStatus.Paid)
            return Result.Failure(FinanceErrors.Invoices.AlreadyPaid);

        if (Status != InvoiceStatus.Approved)
            return Result.Failure(new Shared.Kernel.Errors.Error(
                "Finance.Invoice.CannotPay",
                "Only approved invoices can be marked as paid."));

        Status = InvoiceStatus.Paid;
        PaidAtUtc = DateTime.UtcNow;

        RaiseDomainEvent(new InvoicePaidDomainEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            Id,
            TenantId,
            TotalGross.Amount,
            Currency));

        return Result.Success();
    }
}
