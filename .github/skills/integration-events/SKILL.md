---
name: integration-events
description: >-
  Generează Integration Events pentru comunicare inter-modul în ERP:
  contract în Shared.Contracts, publisher din DomainEvent handler,
  consumer în modulul destinatar, înregistrare MassTransit.
  Module nu se referențiează direct — exclusiv prin IntegrationEvents.
---

# Integration Events

## Când se aplică
Când o acțiune dintr-un modul (Finance) trebuie să declanșeze logică
în alt modul (HR, Inventory, Purchasing) — fără referință directă între module.

## Flux complet

```
Invoice.Approve()                          ← Domain method (Finance.Domain)
  → InvoiceApprovedDomainEvent             ← Domain Event (in-process, sync)
    → InvoiceApprovedDomainEventHandler    ← Application handler (Finance)
      → publică InvoiceApprovedIntegration ← Integration Event (Shared.Contracts)
        → MassTransit (in-memory / RabbitMQ)
          → InventoryReserveConsumer       ← Consumer (Inventory.Application)
          → AccountingJournalConsumer      ← Consumer (Accounting.Application)
```

## Reguli absolute

```
IntegrationEvents definite în Shared.Contracts   — niciodată în modulul sursă
Modulele nu se referențiează direct               — zero ProjectReference cross-modul
Publicare DUPĂ commit tranzacție                  — TransactionBehavior publică after commit
Consumer idempotent                               — același mesaj procesat de N ori = același rezultat
Consumer nu aruncă excepții necontrolate          — prinde și loghează, nu re-throw
Naming: {Entity}{PastTense}IntegrationEvent       — InvoiceApprovedIntegrationEvent
```

---

## 1. Contract — Shared.Contracts

```csharp
// Shared.Contracts/Events/Finance/InvoiceApprovedIntegrationEvent.cs
namespace Shared.Contracts.Events.Finance;

// Record imutabil — serializat de MassTransit
public sealed record InvoiceApprovedIntegrationEvent(
    Guid EventId,
    Guid InvoiceId,
    Guid TenantId,
    Guid CustomerId,
    decimal TotalAmount,
    string Currency,
    Guid ApprovedBy,
    DateTime OccurredAt);
```

**Reguli pentru contracte:**
- Tipuri primitive sau alte records — niciodată entități Domain
- `EventId` și `OccurredAt` obligatorii pe orice IntegrationEvent
- Record imutabil (`sealed record`) — nu clasă
- Namespace: `Shared.Contracts.Events.{Module}`

---

## 2. Publisher — DomainEvent Handler (modulul sursă)

```csharp
// Finance.Application/EventHandlers/InvoiceApprovedDomainEventHandler.cs
internal sealed class InvoiceApprovedDomainEventHandler
    : INotificationHandler<InvoiceApprovedDomainEvent>
{
    private readonly IPublishEndpoint _publishEndpoint;  // MassTransit
    private readonly IInvoiceRepository _repo;

    public InvoiceApprovedDomainEventHandler(
        IPublishEndpoint publishEndpoint,
        IInvoiceRepository repo)
    {
        _publishEndpoint = publishEndpoint;
        _repo = repo;
    }

    public async Task Handle(
        InvoiceApprovedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        // Citim datele complete dacă DomainEvent nu le conține
        var invoice = await _repo.GetByIdAsync(notification.InvoiceId, cancellationToken);
        if (invoice is null) return;

        await _publishEndpoint.Publish(
            new InvoiceApprovedIntegrationEvent(
                EventId:     Guid.NewGuid(),
                InvoiceId:   notification.InvoiceId,
                TenantId:    notification.TenantId,
                CustomerId:  invoice.CustomerId,
                TotalAmount: invoice.TotalAmount,
                Currency:    invoice.Currency,
                ApprovedBy:  notification.ApprovedBy,
                OccurredAt:  notification.OccurredAt),
            cancellationToken);
    }
}
```

---

## 3. Consumer — modulul destinatar

```csharp
// Inventory.Application/EventHandlers/InvoiceApprovedIntegrationEventConsumer.cs
public sealed class InvoiceApprovedIntegrationEventConsumer
    : IConsumer<InvoiceApprovedIntegrationEvent>
{
    private readonly IStockReservationRepository _repo;
    private readonly ILogger<InvoiceApprovedIntegrationEventConsumer> _logger;

    public InvoiceApprovedIntegrationEventConsumer(
        IStockReservationRepository repo,
        ILogger<InvoiceApprovedIntegrationEventConsumer> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<InvoiceApprovedIntegrationEvent> context)
    {
        var evt = context.Message;

        _logger.LogInformation(
            "Processing {EventType} for Invoice {InvoiceId} Tenant {TenantId}",
            nameof(InvoiceApprovedIntegrationEvent),
            evt.InvoiceId,
            evt.TenantId);

        // Idempotență — verifică dacă deja procesat
        var alreadyProcessed = await _repo.ReservationExistsAsync(
            evt.InvoiceId, evt.TenantId, context.CancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogWarning(
                "Duplicate event {EventId} for Invoice {InvoiceId} — skipping",
                evt.EventId, evt.InvoiceId);
            return;
        }

        // Logică business a modulului destinatar
        await _repo.CreateReservationAsync(evt.InvoiceId, evt.TenantId,
            context.CancellationToken);
    }
}
```

---

## 4. Înregistrare MassTransit în modul

```csharp
// Shared.Infrastructure/Extensions/MassTransitExtensions.cs
public static IServiceCollection AddErpMassTransit(
    this IServiceCollection services,
    IConfiguration configuration,
    Action<IBusRegistrationConfigurator> configureConsumers)
{
    services.AddMassTransit(x =>
    {
        // Consumers înregistrați per modul
        configureConsumers(x);

        x.UsingInMemory((ctx, cfg) =>   // InMemory pentru Modular Monolith
        {                               // Schimbat cu RabbitMQ când extragi un modul
            cfg.ConfigureEndpoints(ctx);
        });

        // Pentru producție cu RabbitMQ:
        // x.UsingRabbitMq((ctx, cfg) =>
        // {
        //     cfg.Host(configuration["RabbitMQ:Host"]);
        //     cfg.ConfigureEndpoints(ctx);
        // });
    });

    return services;
}

// Inventory.Infrastructure/InventoryModule.cs
public sealed class InventoryModule : IModuleInstaller
{
    public IServiceCollection Install(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // Înregistrează consumerii acestui modul
        services.AddMassTransit(x =>
        {
            x.AddConsumer<InvoiceApprovedIntegrationEventConsumer>();
            x.AddConsumer<OrderCreatedIntegrationEventConsumer>();
        });

        return services;
    }
}
```

---

## 5. Publicare after commit — TransactionBehavior

```csharp
// Shared.Infrastructure/Behaviors/TransactionBehavior.cs
public sealed class TransactionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ITransactional
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPublishEndpoint _publishEndpoint;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        using var conn = _connectionFactory.Create();
        using var transaction = conn.BeginTransaction();

        try
        {
            var response = await next();

            transaction.Commit();

            // Domain Events publicate DUPĂ commit — nu în tranzacție
            // (colectate de AggregateRoot, trimise de handler)

            return response;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
```

---

## Checklist Integration Event

- [ ] Contract definit în `Shared.Contracts/Events/{Module}/` — nu în modulul sursă
- [ ] `EventId` și `OccurredAt` prezente pe contract
- [ ] Contract = `sealed record` cu tipuri primitive
- [ ] Consumer implementează idempotență (verifică EventId sau business key)
- [ ] Consumer nu re-aruncă excepții — prinde, loghează, returnează
- [ ] Consumer înregistrat în `IModuleInstaller` al modulului destinatar
- [ ] Publicare în DomainEvent handler — nu direct din Command handler
- [ ] Zero referință directă între module în cod C#
