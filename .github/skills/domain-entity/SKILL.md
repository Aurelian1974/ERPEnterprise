---
name: domain-entity
description: >-
  Generează entități Domain corecte pentru ERP: AggregateRoot cu UUIDv7,
  Entity, ValueObject, DomainEvent, invariante business, metode factory.
  Domain layer are zero dependențe externe — nicio referință la Infrastructure.
---

# Domain Entity

## Când se aplică
Când utilizatorul cere să creeze o entitate, un aggregate root, un value object
sau un domain event în layerul Domain al unui modul ERP.

## Reguli absolute

```
Zero dependențe externe în Domain   — niciun NuGet package, nicio referință la Infrastructure
ID generat în constructor           — Uuid.NewDatabaseFriendly(Database.SqlServer) (UUIDv7)
Constructori privați sau protected  — instanțiere exclusiv prin metode factory statice
Setteri privați                     — starea se modifică exclusiv prin metode cu nume business
Invariante în metode, nu în handler — Guard clauses în entitate, nu în Command handler
sealed pe clase concrete            — nicio moștenire necontrolată
Domain Events raised din entitate   — nu din handler
```

---

## Template AggregateRoot

```csharp
// {Module}.Domain/Entities/Invoice.cs
public sealed class Invoice : AggregateRoot
{
    // Proprietăți — setteri privați
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public InvoiceNumber Number { get; private set; }      // Value Object
    public Money TotalAmount { get; private set; }          // Value Object
    public InvoiceStatus Status { get; private set; }
    public DateOnly DueDate { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private readonly List<InvoiceLine> _lines = [];
    public IReadOnlyList<InvoiceLine> Lines => _lines.AsReadOnly();

    // Constructor privat — EF Core / Dapper hydration
    private Invoice() { }

    // Factory method — singura cale de creare
    public static Invoice Create(
        Guid tenantId,
        Guid customerId,
        InvoiceNumber number,
        DateOnly dueDate,
        Guid createdBy)
    {
        // Guard clauses — invariante business
        Guard.Against.Empty(tenantId, nameof(tenantId));
        Guard.Against.Empty(customerId, nameof(customerId));
        Guard.Against.Default(dueDate, nameof(dueDate));

        if (dueDate < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new DomainException(FinanceErrors.InvalidDueDate);

        var invoice = new Invoice
        {
            // ID generat în C# cu UUIDv7 — disponibil înainte de INSERT
            Id         = Uuid.NewDatabaseFriendly(Database.SqlServer),
            TenantId   = tenantId,
            CustomerId = customerId,
            Number     = number,
            TotalAmount = Money.Zero,
            Status     = InvoiceStatus.Draft,
            DueDate    = dueDate,
            CreatedBy  = createdBy,
            CreatedAt  = DateTime.UtcNow
        };

        // Domain Event raised din entitate
        invoice.RaiseDomainEvent(new InvoiceCreatedDomainEvent(invoice.Id, tenantId));

        return invoice;
    }

    // Metode cu nume business — nu setteri direcți
    public void AddLine(InvoiceLine line)
    {
        Guard.Against.Null(line, nameof(line));

        if (Status != InvoiceStatus.Draft)
            throw new DomainException(FinanceErrors.InvoiceNotDraft);

        _lines.Add(line);
        TotalAmount = Money.From(_lines.Sum(l => l.LineTotal.Amount), TotalAmount.Currency);
    }

    public void Approve(Guid approvedBy)
    {
        if (Status != InvoiceStatus.Draft)
            throw new DomainException(FinanceErrors.InvoiceNotDraft);

        if (!_lines.Any())
            throw new DomainException(FinanceErrors.InvoiceHasNoLines);

        Status = InvoiceStatus.Approved;
        RaiseDomainEvent(new InvoiceApprovedDomainEvent(Id, TenantId, approvedBy));
    }

    public void Cancel(Guid cancelledBy)
    {
        if (Status == InvoiceStatus.Cancelled)
            throw new DomainException(FinanceErrors.InvoiceAlreadyCancelled);

        Status = InvoiceStatus.Cancelled;
        RaiseDomainEvent(new InvoiceCancelledDomainEvent(Id, TenantId, cancelledBy));
    }
}
```

## Template AggregateRoot base class

```csharp
// Shared.Kernel/Primitives/AggregateRoot.cs
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents()
        => _domainEvents.Clear();
}

// Shared.Kernel/Primitives/Entity.cs
public abstract class Entity
{
    public Guid Id { get; protected set; }

    protected Entity() { }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return Id == other.Id;
    }

    public override int GetHashCode() => Id.GetHashCode();
}
```

## Template Entity (non-aggregate)

```csharp
// {Module}.Domain/Entities/InvoiceLine.cs
public sealed class InvoiceLine : Entity
{
    public Guid InvoiceId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public decimal VatRate { get; private set; }
    public Money LineTotal { get; private set; }
    public int SortOrder { get; private set; }

    private InvoiceLine() { }

    // ID = BIGINT IDENTITY în DB, dar pentru domain logic folosim Guid temporar
    // Repository-ul ignoră Id-ul la INSERT (îl generează SQL Server)
    public static InvoiceLine Create(
        Guid invoiceId,
        Guid tenantId,
        Guid productId,
        string description,
        decimal quantity,
        Money unitPrice,
        decimal vatRate,
        int sortOrder)
    {
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));
        Guard.Against.Negative(vatRate, nameof(vatRate));

        var lineTotal = Money.From(quantity * unitPrice.Amount, unitPrice.Currency);

        return new InvoiceLine
        {
            Id          = Guid.NewGuid(),   // temporar — ignorat la INSERT (BIGINT IDENTITY)
            InvoiceId   = invoiceId,
            TenantId    = tenantId,
            ProductId   = productId,
            Description = description,
            Quantity    = quantity,
            UnitPrice   = unitPrice,
            VatRate     = vatRate,
            LineTotal   = lineTotal,
            SortOrder   = sortOrder
        };
    }
}
```

## Template ValueObject

```csharp
// Shared.Kernel/Primitives/ValueObject.cs
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType()) return false;
        return ((ValueObject)obj).GetEqualityComponents()
            .SequenceEqual(GetEqualityComponents());
    }

    public override int GetHashCode()
        => GetEqualityComponents()
            .Aggregate(default(HashCode), (h, c) => { h.Add(c); return h; })
            .ToHashCode();

    public static bool operator ==(ValueObject? a, ValueObject? b)
        => a?.Equals(b) ?? b is null;

    public static bool operator !=(ValueObject? a, ValueObject? b)
        => !(a == b);
}

// {Module}.Domain/ValueObjects/Money.cs
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public static Money Zero => new(0, "RON");

    private Money(decimal amount, string currency)
    {
        Amount   = amount;
        Currency = currency;
    }

    public static Money From(decimal amount, string currency)
    {
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        return new Money(amount, currency);
    }

    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new DomainException(SharedErrors.CurrencyMismatch);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(decimal factor)
    {
        Guard.Against.Negative(factor, nameof(factor));
        return new Money(Amount * factor, Currency);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:F2} {Currency}";
}

// {Module}.Domain/ValueObjects/InvoiceNumber.cs
public sealed class InvoiceNumber : ValueObject
{
    public string Value { get; }

    private InvoiceNumber(string value) => Value = value;

    public static InvoiceNumber From(string value)
    {
        Guard.Against.NullOrWhiteSpace(value, nameof(value));
        if (value.Length > 50)
            throw new DomainException(FinanceErrors.InvoiceNumberTooLong);
        return new InvoiceNumber(value.Trim().ToUpperInvariant());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
```

## Template DomainEvent

```csharp
// Shared.Kernel/Abstractions/IDomainEvent.cs
public interface IDomainEvent : INotification
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
}

// {Module}.Domain/DomainEvents/InvoiceApprovedDomainEvent.cs
public sealed record InvoiceApprovedDomainEvent(
    Guid InvoiceId,
    Guid TenantId,
    Guid ApprovedBy) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// {Module}.Application/EventHandlers/InvoiceApprovedDomainEventHandler.cs
// Handler în Application layer — nu în Domain
internal sealed class InvoiceApprovedDomainEventHandler
    : INotificationHandler<InvoiceApprovedDomainEvent>
{
    private readonly IPublisher _publisher;   // MassTransit — publică IntegrationEvent

    public InvoiceApprovedDomainEventHandler(IPublisher publisher)
        => _publisher = publisher;

    public async Task Handle(
        InvoiceApprovedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        // Transformă DomainEvent în IntegrationEvent (cross-module)
        await _publisher.Publish(
            new InvoiceApprovedIntegrationEvent(
                notification.InvoiceId,
                notification.TenantId,
                notification.ApprovedBy),
            cancellationToken);
    }
}
```

## Template Enum

```csharp
// {Module}.Domain/Enums/InvoiceStatus.cs
public enum InvoiceStatus : byte    // byte = TINYINT în SQL Server
{
    Draft     = 1,
    Approved  = 2,
    Paid      = 3,
    Cancelled = 4
}
```

## Template Errors

```csharp
// {Module}.Domain/Errors/FinanceErrors.cs
public static class FinanceErrors
{
    // Format: "{module}.{entity}.{error_code}"
    public static readonly Error InvalidDueDate =
        new("finance.invoice.invalid_due_date",
            "Due date cannot be in the past.");

    public static readonly Error InvoiceNotDraft =
        new("finance.invoice.not_draft",
            "Operation allowed only on Draft invoices.");

    public static readonly Error InvoiceHasNoLines =
        new("finance.invoice.no_lines",
            "Invoice must have at least one line.");

    public static readonly Error InvoiceAlreadyCancelled =
        new("finance.invoice.already_cancelled",
            "Invoice is already cancelled.");

    public static readonly Error InvoiceNumberTooLong =
        new("finance.invoice.number_too_long",
            "Invoice number cannot exceed 50 characters.");

    public static Error InvoiceNotFound(Guid id) =>
        new("finance.invoice.not_found",
            $"Invoice '{id}' was not found.");
}
```

## Reguli obligatorii

```
ID aggregate root   — Uuid.NewDatabaseFriendly(Database.SqlServer) în constructor/factory
ID child entity     — Guid.NewGuid() temporar (SQL Server generează BIGINT la INSERT)
Constructori        — private sau protected, instanțiere prin factory method static
Setteri             — private pe toate proprietățile
Invariante          — Guard.Against în factory/metodă business, nu în handler
Domain Events       — raised din entitate, nu din handler
Enums               — : byte (TINYINT în SQL), valori explicite de la 1
Errors              — static class în Domain, format "{module}.{entity}.{code}"
Zero dependențe     — Domain nu referențiază Infrastructure, Application, sau NuGet extern
```
