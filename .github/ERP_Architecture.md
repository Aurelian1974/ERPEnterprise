# ERP Platform — Architecture & Project Structure

> **Stack**: .NET 10 · React 19 + TypeScript · SQL Server 2025 · Dapper  
> **Pattern**: Clean Architecture + Vertical Slice Architecture (VSA) within a Modular Monolith  
> **Principle**: Start monolith, decompose to services only when pain demands it — premature microservices kill ERPs.

---

## Table of Contents

1. [Architecture Philosophy](#architecture-philosophy)
2. [Tech Stack](#tech-stack)
3. [Project Structure](#project-structure)
4. [Backend Architecture](#backend-architecture)
5. [Frontend Architecture](#frontend-architecture)
6. [Database Strategy](#database-strategy)
7. [Cross-Cutting Concerns](#cross-cutting-concerns)
8. [Testing Strategy](#testing-strategy)
9. [CI/CD & Deployment](#cicd--deployment)
10. [Development Setup](#development-setup)
11. [Conventions & Standards](#conventions--standards)
12. [Architecture Decision Records (ADRs)](#architecture-decision-records)

---

## Architecture Philosophy

### Modular Monolith + Vertical Slices

```
Modular Monolith:     Each business domain (Finance, HR, Inventory…) is a
                      self-contained module with its own DB schema, models,
                      and API surface. Modules communicate via in-process
                      Domain Events — NOT direct references.

Vertical Slices:      Each feature (CreateInvoice, ApproveLeaveRequest…) owns
                      its entire stack: endpoint → handler → SQL → response.
                      No shared anemic services. Change one feature, touch one folder.

Clean Architecture:   Dependency rule enforced at module level. Domain has
                      zero dependencies. Application depends only on Domain.
                      Infrastructure depends on Application.
```

### Why Not Microservices?

- ERP data is deeply relational — cross-module transactions (Finance ↔ Inventory ↔ HR) are trivial in-process and brutal over the network.
- Microservices overhead (service mesh, distributed tracing, eventual consistency, saga orchestration) is unjustifiable until you have 50+ developers.
- Modular Monolith gives you the separation of concerns without the operational tax. You can extract a module to a service later with minimal refactoring.

### Why Dapper Over EF Core?

- ERP requires complex, tuned SQL — stored procedures, CTEs, window functions, dynamic pivots.
- EF Core's abstraction leaks exactly when you need performance (N+1, cartesian explosion, missing indexes).
- Dapper gives full SQL control with minimal overhead. The generated SQL is always exactly what you wrote.
- Schema migrations are done with DbUp (versioned SQL scripts), not EF migrations.

---

## Tech Stack

### Backend

| Concern | Technology | Version | Notes |
|---|---|---|---|
| Runtime | .NET | 10 LTS | Controllers cu [ApiController] — ERP are 200+ endpoints, Controllers câștigă la scară |
| ORM | Dapper | 2.1+ | Apelează exclusiv obiecte SQL (SP, View, TVF) — zero SQL inline în C# |
| CQRS / Mediator | MediatR | 12.x | Commands, Queries, Notifications |
| Validation | FluentValidation | 11.x | Pipeline behavior |
| Migrations | DbUp | 5.x | Versioned SQL scripts, runs at startup |
| Background Jobs | Hangfire | 1.8.x | Scheduled + fire-and-forget, SQL Server storage |
| Auth | ASP.NET Core Identity + JWT | — | RBAC + resource-level permissions |
| Logging | Serilog | 4.x | Structured logs → Seq / OpenSearch |
| Observability | OpenTelemetry | 1.9+ | Traces + Metrics → Jaeger / Prometheus |
| Resilience | Polly | 8.x | Retry, circuit breaker for external calls |
| API Docs | Scalar / Swagger | — | OpenAPI 3.1 |
| Mapping | Mapster | 7.x | Faster than AutoMapper, source-gen friendly |
| Feature Flags | Microsoft.FeatureManagement | 3.x | Per-tenant flag overrides |
| ID Generation | UUIDNext | 3.x | UUIDv7 — time-ordered GUID generat în C# |
| Health Checks | AspNetCore.HealthChecks | — | DB, queue, external services |
| Rate Limiting | ASP.NET Core built-in | .NET 10 | Per-tenant, per-endpoint |

### Frontend

| Concern | Technology | Version | Notes |
|---|---|---|---|
| Framework | React | 19 | Server Components ready |
| Language | TypeScript | 5.x | Strict mode |
| Build | Vite | 6.x | — |
| State (server) | TanStack Query | 5.x | Cache, mutations, optimistic updates |
| State (client) | Zustand | 5.x | UI state, user session |
| Routing | TanStack Router | 1.x | Type-safe routes |
| Forms | React Hook Form + Zod | — | Schema-driven, Zod shared with BE types |
| UI Components | shadcn/ui + Radix | — | Unstyled, accessible primitives |
| Styling | Tailwind CSS | 4.x | — |
| Tables/Grids | TanStack Table | 8.x | Virtual rows for large datasets |
| Charts | Recharts | 2.x | ERP dashboards |
| Icons | Lucide React | — | — |
| i18n | react-i18next | — | Multi-language from day 1 |
| API Client | Axios + openapi-typescript | — | Generated from OpenAPI spec |

### Infrastructure

| Concern | Technology | Notes |
|---|---|---|
| Database | SQL Server 2025 | Per-module schemas |
| Cache | Redis (StackExchange.Redis) | Distributed cache, session, rate limit counters |
| Message Bus | MassTransit (in-memory → RabbitMQ later) | Decoupled module events |
| File Storage | MinIO (S3-compatible) | Documents, exports, attachments |
| Email | MailKit + SMTP | Transactional email |
| Reverse Proxy | NGINX | SSL termination, static assets |

---

## Project Structure

```
erp/
├── .github/
│   └── workflows/
│       ├── ci.yml                    # Build, test, lint on every PR
│       ├── cd-staging.yml            # Deploy to staging on merge to main
│       └── cd-production.yml         # Deploy to production on tag
│
├── docs/
│   ├── adr/                          # Architecture Decision Records
│   │   ├── 001-modular-monolith.md
│   │   ├── 002-dapper-over-efcore.md
│   │   └── 003-vertical-slices.md
│   ├── modules/                      # Module-level documentation
│   └── api/                          # Generated OpenAPI specs
│
├── src/
│   ├── Api/                          # Host project — composition root
│   │   ├── Program.cs                # Wires up all modules
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── appsettings.Production.json
│   │   ├── Middleware/
│   │   │   ├── ExceptionHandlingMiddleware.cs
│   │   │   ├── TenantResolutionMiddleware.cs
│   │   │   └── RequestLoggingMiddleware.cs
│   │   └── Extensions/
│   │       ├── ModuleExtensions.cs   # Registers all modules
│   │       └── OpenTelemetryExtensions.cs
│   │
│   ├── Shared/
│   │   ├── Shared.Kernel/            # Pure domain primitives — NO dependencies
│   │   │   ├── Abstractions/
│   │   │   │   ├── ICommand.cs
│   │   │   │   ├── IQuery.cs
│   │   │   │   ├── IRepository.cs
│   │   │   │   └── IDomainEvent.cs
│   │   │   ├── Primitives/
│   │   │   │   ├── Entity.cs         # Base entity with Id + domain events
│   │   │   │   ├── AggregateRoot.cs
│   │   │   │   ├── ValueObject.cs
│   │   │   │   └── Result.cs         # Result<T> pattern — no exceptions for business errors
│   │   │   ├── Guards/
│   │   │   │   └── Guard.cs          # Guard clauses
│   │   │   └── Errors/
│   │   │       ├── Error.cs
│   │   │       └── ErrorCodes.cs
│   │   │
│   │   ├── Shared.Infrastructure/    # Cross-cutting technical concerns
│   │   │   ├── Database/
│   │   │   │   ├── DbConnectionFactory.cs    # IDbConnection factory (Dapper)
│   │   │   │   ├── UnitOfWork.cs             # Transaction wrapper
│   │   │   │   ├── Migrations/               # DbUp entry point
│   │   │   │   │   └── MigrationRunner.cs
│   │   │   │   └── SqlTypeHandlers/          # Dapper type handlers (DateOnly, etc.)
│   │   │   ├── Auth/
│   │   │   │   ├── JwtTokenService.cs
│   │   │   │   ├── PermissionAuthorizationHandler.cs
│   │   │   │   └── CurrentUserService.cs     # ICurrentUser implementation
│   │   │   ├── Caching/
│   │   │   │   └── CacheService.cs           # Redis wrapper
│   │   │   ├── Behaviors/                    # MediatR pipeline behaviors
│   │   │   │   ├── ValidationBehavior.cs
│   │   │   │   ├── LoggingBehavior.cs
│   │   │   │   ├── CachingBehavior.cs
│   │   │   │   ├── AuditBehavior.cs
│   │   │   │   └── TransactionBehavior.cs
│   │   │   ├── Audit/
│   │   │   │   └── AuditService.cs           # Writes to audit_log table
│   │   │   ├── FileStorage/
│   │   │   │   └── FileStorageService.cs     # MinIO wrapper
│   │   │   ├── Email/
│   │   │   │   └── EmailService.cs
│   │   │   ├── MultiTenancy/
│   │   │   │   ├── TenantContext.cs          # Scoped: current tenant
│   │   │   │   └── TenantResolver.cs         # From JWT claim / subdomain / header
│   │   │   └── Extensions/
│   │   │       └── ServiceCollectionExtensions.cs
│   │   │
│   │   └── Shared.Contracts/         # Public contracts for inter-module events
│   │       ├── Events/
│   │       │   ├── Finance/
│   │       │   │   └── InvoicePaidEvent.cs
│   │       │   ├── HR/
│   │       │   │   └── EmployeeCreatedEvent.cs
│   │       │   └── Inventory/
│   │       │       └── StockLevelChangedEvent.cs
│   │       └── IntegrationEvents/    # For future external messaging
│   │
│   └── Modules/
│       │
│       ├── Finance/                  # Example: Finance module
│       │   ├── Finance.Domain/
│       │   │   ├── Entities/
│       │   │   │   ├── Invoice.cs
│       │   │   │   └── InvoiceLine.cs
│       │   │   ├── ValueObjects/
│       │   │   │   ├── Money.cs
│       │   │   │   └── InvoiceNumber.cs
│       │   │   ├── Enums/
│       │   │   │   └── InvoiceStatus.cs
│       │   │   ├── DomainEvents/
│       │   │   │   └── InvoiceApprovedDomainEvent.cs
│       │   │   └── Errors/
│       │   │       └── FinanceErrors.cs
│       │   │
│       │   ├── Finance.Application/
│       │   │   ├── Features/                 # Vertical slices
│       │   │   │   ├── Invoices/
│       │   │   │   │   ├── Create/
│       │   │   │   │   │   ├── CreateInvoiceCommand.cs
│       │   │   │   │   │   ├── CreateInvoiceCommandHandler.cs
│       │   │   │   │   │   ├── CreateInvoiceRequest.cs
│       │   │   │   │   │   ├── CreateInvoiceResponse.cs
│       │   │   │   │   │   └── CreateInvoiceValidator.cs
│       │   │   │   │   ├── Approve/
│       │   │   │   │   │   ├── ApproveInvoiceCommand.cs
│       │   │   │   │   │   ├── ApproveInvoiceCommandHandler.cs
│       │   │   │   │   │   └── ApproveInvoiceValidator.cs
│       │   │   │   │   ├── GetById/
│       │   │   │   │   │   ├── GetInvoiceByIdQuery.cs
│       │   │   │   │   │   ├── GetInvoiceByIdQueryHandler.cs
│       │   │   │   │   │   └── InvoiceDetailDto.cs
│       │   │   │   │   └── List/
│       │   │   │   │       ├── ListInvoicesQuery.cs
│       │   │   │   │       ├── ListInvoicesQueryHandler.cs
│       │   │   │   │       ├── InvoiceListDto.cs
│       │   │   │   │       └── InvoiceFilters.cs
│       │   │   │   └── Reports/
│       │   │   │       └── AgingReport/
│       │   │   │           ├── InvoiceAgingReportQuery.cs
│       │   │   │           └── InvoiceAgingReportQueryHandler.cs
│       │   │   ├── Abstractions/
│       │   │   │   └── IFinanceRepository.cs
│       │   │   └── EventHandlers/            # Handles events from OTHER modules
│       │   │       └── EmployeeCreatedEventHandler.cs
│       │   │
│       │   ├── Finance.Infrastructure/
│       │   │   ├── Repositories/
│       │   │   │   └── InvoiceRepository.cs  # Dapper — pure SQL
│       │   │   ├── Database/
│       │   │   │   └── Migrations/           # DbUp SQL scripts
│       │   │   │       ├── 20240101_001_CreateInvoicesTable.sql
│       │   │   │       ├── 20240115_002_AddInvoiceStatusIndex.sql
│       │   │   │       └── 20240201_003_CreateInvoiceLinesTable.sql
│       │   │   ├── StoredProcedures/         # CREATE OR ALTER, always run de DbUp
│       │   │   │   ├── usp_GetInvoiceAging.sql
│       │   │   │   └── usp_ListInvoicesPaged.sql
│       │   │   └── FinanceModule.cs          # Module registration (IModuleInstaller)
│       │   │
│       │   └── Finance.Api/                  # Endpoints for this module
│       │       ├── Endpoints/
│       │       │   ├── InvoiceEndpoints.cs   # Controller pentru această resursă
│       │       │   └── ReportEndpoints.cs
│       │       └── Finance.Api.csproj
│       │
│       ├── HR/                               # Same structure as Finance
│       ├── Inventory/
│       ├── Purchasing/
│       ├── Sales/
│       └── Administration/                   # Tenants, Users, RBAC, Settings
│           ├── Administration.Domain/
│           ├── Administration.Application/
│           │   └── Features/
│           │       ├── Users/
│           │       ├── Roles/
│           │       ├── Permissions/
│           │       └── Tenants/
│           ├── Administration.Infrastructure/
│           └── Administration.Api/
│
├── frontend/
│   ├── src/
│   │   ├── api/                      # Generated API client (openapi-typescript)
│   │   │   └── generated/
│   │   ├── app/                      # TanStack Router — file-based routes
│   │   │   ├── routes/
│   │   │   │   ├── __root.tsx        # Root layout
│   │   │   │   ├── _auth/            # Auth layout (JWT guard)
│   │   │   │   │   ├── finance/
│   │   │   │   │   │   ├── invoices/
│   │   │   │   │   │   │   ├── index.tsx
│   │   │   │   │   │   │   ├── $id.tsx
│   │   │   │   │   │   │   └── new.tsx
│   │   │   │   │   │   └── reports/
│   │   │   │   │   ├── hr/
│   │   │   │   │   ├── inventory/
│   │   │   │   │   └── settings/
│   │   │   │   └── _public/          # Login, reset password
│   │   │   └── router.ts
│   │   ├── components/
│   │   │   ├── ui/                   # shadcn/ui components (do not edit)
│   │   │   ├── common/               # App-level shared components
│   │   │   │   ├── DataTable/        # TanStack Table wrapper
│   │   │   │   ├── FormField/        # RHF + Zod wrapper
│   │   │   │   ├── PageHeader/
│   │   │   │   ├── StatusBadge/
│   │   │   │   └── ConfirmDialog/
│   │   │   └── layout/
│   │   │       ├── AppShell.tsx
│   │   │       ├── Sidebar.tsx
│   │   │       └── Breadcrumbs.tsx
│   │   ├── features/                 # Feature modules — mirror backend VSA
│   │   │   ├── finance/
│   │   │   │   ├── invoices/
│   │   │   │   │   ├── api.ts        # TanStack Query hooks
│   │   │   │   │   ├── schemas.ts    # Zod schemas
│   │   │   │   │   ├── types.ts
│   │   │   │   │   ├── InvoiceList.tsx
│   │   │   │   │   ├── InvoiceDetail.tsx
│   │   │   │   │   └── InvoiceForm.tsx
│   │   │   │   └── reports/
│   │   │   ├── hr/
│   │   │   ├── inventory/
│   │   │   └── administration/
│   │   ├── store/                    # Zustand stores
│   │   │   ├── auth.store.ts
│   │   │   ├── ui.store.ts
│   │   │   └── tenant.store.ts
│   │   ├── lib/
│   │   │   ├── axios.ts              # Axios instance + interceptors
│   │   │   ├── queryClient.ts        # TanStack Query config
│   │   │   └── utils.ts
│   │   ├── i18n/
│   │   │   ├── index.ts
│   │   │   └── locales/
│   │   │       ├── en.json
│   │   │       └── ro.json
│   │   └── hooks/
│   │       ├── useCurrentUser.ts
│   │       ├── usePermission.ts      # RBAC check hook
│   │       └── useTenant.ts
│   ├── index.html
│   ├── vite.config.ts
│   ├── tailwind.config.ts
│   ├── tsconfig.json
│   └── package.json
│
├── tests/
│   ├── Unit/
│   │   ├── Finance.Domain.Tests/
│   │   ├── Finance.Application.Tests/
│   │   └── Shared.Kernel.Tests/
│   ├── Integration/
│   │   ├── Finance.Integration.Tests/  # SQL Server LocalDB — real SQL Server, zero config
│   │   └── Administration.Integration.Tests/
│   └── E2E/
│       └── Playwright/
│           ├── finance/
│           └── auth/
│
├── database/
│   ├── schemas.sql                   # Initial schema creation (run once)
│   └── seed/
│       ├── dev-seed.sql              # Development seed data
│       └── test-seed.sql             # Test data
│
├── .editorconfig
├── .gitignore
├── global.json                       # Pins .NET SDK version
├── Directory.Build.props             # Central package versions (CPM)
├── Directory.Packages.props
├── erp.sln
└── README.md
```

---

## Backend Architecture

### Module Contract

Each module implements `IModuleInstaller`:

```csharp
// Shared.Kernel
public interface IModuleInstaller
{
    IServiceCollection Install(IServiceCollection services, IConfiguration configuration);
    IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app);
}

// Finance module
public sealed class FinanceModule : IModuleInstaller
{
    public IServiceCollection Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(FinanceModule).Assembly));
        services.RunMigrations(configuration); // DbUp
        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1/finance").MapFinanceEndpoints();
        return app;
    }
}
```

### Vertical Slice Example — Create Invoice

```
POST /api/v1/finance/invoices
↓
InvoiceEndpoints.cs          — maps route, extracts request, calls ISender
CreateInvoiceCommand.cs      — record with all input data
ValidationBehavior.cs        — FluentValidation (pipeline)
AuditBehavior.cs             — writes to audit_log (pipeline)
TransactionBehavior.cs       — wraps in SQL transaction (pipeline)
CreateInvoiceCommandHandler.cs → Invoice.Create() domain method
                               → InvoiceRepository.InsertAsync(invoice)
                               → publishes InvoiceCreatedDomainEvent
InvoiceCreatedDomainEventHandler.cs → sends email, updates counters
CreateInvoiceResponse.cs     — returned to client
```

### Result Pattern (no exception-based business logic)

```csharp
// Result.cs in Shared.Kernel
public sealed class Result<T>
{
    public T? Value { get; }
    public Error Error { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public static Result<T> Success(T value) => new(value, Error.None, true);
    public static Result<T> Failure(Error error) => new(default, error, false);
}

// Usage in handler
public async Task<Result<Guid>> Handle(CreateInvoiceCommand command, CancellationToken ct)
{
    var customer = await _repo.GetCustomerAsync(command.CustomerId, ct);
    if (customer is null)
        return Result<Guid>.Failure(FinanceErrors.CustomerNotFound(command.CustomerId));

    var invoice = Invoice.Create(command.CustomerId, command.Lines);
    await _repo.InsertAsync(invoice, ct);
    return Result<Guid>.Success(invoice.Id);
}
```

### MediatR Pipeline Behaviors (order matters)

```
1. LoggingBehavior        — logs request start/end/duration
2. ValidationBehavior     — FluentValidation, throws ValidationException
3. AuthorizationBehavior  — checks IAuthorizeRequest permissions
4. CachingBehavior        — returns cached result for IQueryCacheable
5. TransactionBehavior    — begins SQL transaction for ITransactional commands
6. AuditBehavior          — writes audit log entry after success
```

### RBAC — Permission Model

```
Tenant
  └── User → Roles (many-to-many)
              └── Role → Permissions (many-to-many)
                         └── Permission: "finance.invoices.approve"
                                         "hr.employees.view"
                                         "inventory.stock.adjust"

Resource-level: PermissionPolicy("[module].[resource].[action]")
```

### Multi-Tenancy

- **Strategy**: Schema-per-tenant for large tenants, shared schema with `TenantId` column for smaller ones.
- **Tenant resolution**: JWT claim `tid` → `TenantContext` (scoped service).
- **Dapper queries**: All repositories receive `TenantContext` via DI. Queries always include `WHERE tenant_id = @TenantId`.
- **DbUp migrations**: Run per-tenant on a background job schedule (not at startup for large SaaS).

### Domain Events vs Integration Events

```
DomainEvent:        In-process, synchronous (within the same transaction).
                    InvoiceApprovedDomainEvent → updates internal counters.
                    Handler registered via MediatR INotificationHandler.

IntegrationEvent:   Cross-module, published AFTER transaction commit.
                    InvoicePaidIntegrationEvent → picked up by HR, Inventory.
                    Published via MassTransit (in-memory transport for monolith,
                    RabbitMQ when you extract a module).
```

---

## Frontend Architecture

### Data Fetching Pattern

```typescript
// features/finance/invoices/api.ts
export const invoiceKeys = {
  all: ['invoices'] as const,
  list: (filters: InvoiceFilters) => [...invoiceKeys.all, 'list', filters] as const,
  detail: (id: string) => [...invoiceKeys.all, 'detail', id] as const,
};

export const useInvoices = (filters: InvoiceFilters) =>
  useQuery({
    queryKey: invoiceKeys.list(filters),
    queryFn: () => api.get<PagedResult<InvoiceListDto>>('/finance/invoices', { params: filters }),
    staleTime: 30_000,
  });

export const useCreateInvoice = () =>
  useMutation({
    mutationFn: (data: CreateInvoiceRequest) => api.post('/finance/invoices', data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: invoiceKeys.all }),
  });
```

### Permission Guard Hook

```typescript
// hooks/usePermission.ts
export const usePermission = (permission: string): boolean => {
  const { permissions } = useAuthStore();
  return permissions.includes(permission) || permissions.includes('*');
};

// Usage in component
const canApprove = usePermission('finance.invoices.approve');
```

### Form Pattern (React Hook Form + Zod)

```typescript
// features/finance/invoices/schemas.ts
export const createInvoiceSchema = z.object({
  customerId: z.string().uuid(),
  dueDate: z.date(),
  lines: z.array(z.object({
    productId: z.string().uuid(),
    quantity: z.number().positive(),
    unitPrice: z.number().positive(),
  })).min(1, 'At least one line is required'),
});

export type CreateInvoiceForm = z.infer<typeof createInvoiceSchema>;
```

---

## Database Strategy

### Schema Organization

```sql
-- Per-module schemas (isolation without separate DBs)
CREATE SCHEMA finance;
CREATE SCHEMA hr;
CREATE SCHEMA inventory;
CREATE SCHEMA purchasing;
CREATE SCHEMA sales;
CREATE SCHEMA administration;
CREATE SCHEMA audit;          -- Centralized audit log
CREATE SCHEMA hangfire;       -- Background jobs
```

### Primary Key Strategy — Hybrid

Regula simplă: tipul ID-ului urmează rolul tabelului, nu o convenție uniformă.

| Tip tabel | PK | Motivul |
|---|---|---|
| Aggregate roots (Invoice, Employee, Product…) | `UNIQUEIDENTIFIER` UUIDv7 | Generat în C# înainte de INSERT — esențial pentru Domain Events |
| Child / line tables (InvoiceLines, JournalLines…) | `BIGINT IDENTITY` | Join performance pe volume mari, niciodată expus în API |
| Append-only / audit / log | `BIGINT IDENTITY` | Insert speed maxim, secvențial prin natură |
| Tabele de referință mici (Countries, Currencies…) | `BIGINT IDENTITY` sau natural key (`CHAR(3)`) | Human-readable, volum neglijabil |

**De ce nu GUID random (`NEWID()`)?** Index clustered aleatoriu = page splits la fiecare insert = fragmentare masivă. Exclus.

**De ce nu `NEWSEQUENTIALID()`?** Generat de SQL Server, nu în aplicație. Nu poți crea ID-ul înainte de INSERT, deci nu poți publica Domain Events cu ID-ul entității înainte ca aceasta să existe în DB.

**De ce UUIDv7?** Time-ordered (primii 48 biți = timestamp ms), generat în C#, aproape secvențial — fragmentare neglijabilă pe clustered index. NuGet: `UUIDNext`.

```csharp
// Entity base class — ID generat în C# înainte de orice INSERT
public abstract class AggregateRoot
{
    public Guid Id { get; protected set; } = Uuid.NewDatabaseFriendly(Database.SqlServer); // UUIDv7
}

// Avantaj concret: ID disponibil ÎNAINTE de insert
var invoice = new Invoice(customerId, lines);
// invoice.Id există deja — pot publica evenimentul, pot returna ID-ul clientului
_domainEvents.Raise(new InvoiceCreatedEvent(invoice.Id));
await _repo.InsertAsync(invoice); // INSERT cu ID pre-generat
```

```sql
-- Aggregate root — UNIQUEIDENTIFIER, ID vine din aplicație (UUIDv7)
CREATE TABLE finance.invoices (
    id              UNIQUEIDENTIFIER    NOT NULL,  -- setat din C#, nu DEFAULT
    tenant_id       UNIQUEIDENTIFIER    NOT NULL,
    customer_id     UNIQUEIDENTIFIER    NOT NULL,
    invoice_number  NVARCHAR(50)        NOT NULL,
    status          TINYINT             NOT NULL DEFAULT 1,
    due_date        DATE                NOT NULL,
    total_amount    DECIMAL(18,4)       NOT NULL DEFAULT 0,
    created_at      DATETIME2(7)        NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by      UNIQUEIDENTIFIER    NOT NULL,
    CONSTRAINT PK_invoices PRIMARY KEY CLUSTERED (id),
    CONSTRAINT UQ_invoices_number UNIQUE (tenant_id, invoice_number),
    INDEX IX_invoices_tenant_status (tenant_id, status) INCLUDE (invoice_number, due_date, total_amount),
    INDEX IX_invoices_customer (tenant_id, customer_id)
);

-- Child table — BIGINT IDENTITY, join masiv pe invoice_id
CREATE TABLE finance.invoice_lines (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    invoice_id      UNIQUEIDENTIFIER    NOT NULL,
    product_id      UNIQUEIDENTIFIER    NOT NULL,
    description     NVARCHAR(500)       NOT NULL,
    quantity        DECIMAL(18,4)       NOT NULL,
    unit_price      DECIMAL(18,4)       NOT NULL,
    vat_rate        DECIMAL(5,4)        NOT NULL DEFAULT 0.19,
    line_total      AS (quantity * unit_price)  PERSISTED,
    sort_order      INT                 NOT NULL DEFAULT 0,
    CONSTRAINT PK_invoice_lines PRIMARY KEY CLUSTERED (id),
    CONSTRAINT FK_invoice_lines_invoices
        FOREIGN KEY (invoice_id) REFERENCES finance.invoices(id) ON DELETE CASCADE,
    INDEX IX_invoice_lines_invoice (invoice_id)  -- acoperit pentru JOIN standard
);

-- Append-only / volum mare — BIGINT IDENTITY
CREATE TABLE audit.audit_log (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    tenant_id       UNIQUEIDENTIFIER    NOT NULL,
    user_id         UNIQUEIDENTIFIER    NOT NULL,
    user_name       NVARCHAR(256)       NOT NULL,
    action          NVARCHAR(100)       NOT NULL,
    entity_type     NVARCHAR(100)       NOT NULL,
    entity_id       NVARCHAR(100)       NULL,      -- stringified GUID sau număr
    old_values      NVARCHAR(MAX)       NULL,
    new_values      NVARCHAR(MAX)       NULL,
    ip_address      NVARCHAR(45)        NULL,
    user_agent      NVARCHAR(512)       NULL,
    created_at      DATETIME2(7)        NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_audit_log PRIMARY KEY CLUSTERED (id),
    INDEX IX_audit_log_tenant_entity (tenant_id, entity_type, entity_id),
    INDEX IX_audit_log_created_at (created_at DESC)
);
```

### DbUp Migration Convention

```
Naming:  YYYYMMDD_NNN_Description.sql
Order:   DbUp discovers files embedded as resources, ordered lexicographically
Journal: __SchemaVersions table per schema (or one central)

Location per module:
  Finance.Infrastructure/Database/Migrations/
    20240101_001_CreateInvoicesTable.sql
    20240115_002_AddInvoiceStatusIndex.sql
    20240201_003_CreateInvoiceLinesTable.sql
```

### DbUp at Startup (deploy to DB on build)

```csharp
// MigrationRunner.cs
public static class MigrationRunner
{
    public static void RunMigrations(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")!;

        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(FinanceModule).Assembly,
                s => s.Contains(".Migrations."))
            .WithTransaction()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"Migration failed: {result.Error}");
    }
}
```

### Stored Procedures (complex read-side queries)

```
Location:  Finance.Infrastructure/Database/StoredProcedures/
           CREATE OR ALTER — idempotente, aplicate la fiecare deploy (always run, not journaled)

Naming:    usp_{Action}{Entity}  — schema asigură contextul modulului
           finance.usp_GetInvoiceAging
           inventory.usp_GetStockMovementHistory
```

---

## Cross-Cutting Concerns

### Structured Logging (Serilog)

```csharp
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "ERP")
    .Enrich.WithProperty("Environment", env.EnvironmentName)
    .WriteTo.Console(new CompactJsonFormatter())
    .WriteTo.Seq(seqUrl)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .CreateLogger();

// Every request automatically enriched with:
// TenantId, UserId, TraceId, RequestId
```

### OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddSqlClientInstrumentation(o => o.SetDbStatementForText = true)
        .AddHangfireInstrumentation()
        .AddOtlpExporter()) // → Jaeger
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter()); // → Grafana
```

### Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sql-server", tags: ["db"])
    .AddRedis(redisConnectionString, name: "redis", tags: ["cache"])
    .AddHangfire(c => c.MinimumAvailableServers = 1, name: "hangfire")
    .AddUrlGroup(new Uri(config["MinIO:Endpoint"]!), name: "minio");

// Endpoints
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = h => h.Tags.Contains("db") });
app.MapHealthChecks("/health/full");
```

### Background Jobs (Hangfire)

```csharp
// Scheduled jobs — defined at module registration
RecurringJob.AddOrUpdate<InvoiceReminderJob>(
    "invoice-reminders",
    j => j.SendRemindersAsync(),
    Cron.Daily(8)); // 08:00 every day

RecurringJob.AddOrUpdate<StockReplenishmentJob>(
    "stock-replenishment-check",
    j => j.CheckLevelsAsync(),
    Cron.Hourly());

// Fire-and-forget (from command handler)
_backgroundJobs.Enqueue<PdfGeneratorJob>(j => j.GenerateInvoicePdfAsync(invoiceId));
```

### Rate Limiting

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("api", ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: ctx.User.FindFirst("tid")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString(),
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 500,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
            }));

    o.AddPolicy("auth", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15),
            }));
});
```

---

## Testing Strategy

### Test Pyramid

```
                    ┌──────────┐
                    │   E2E    │   ~20 tests  — Playwright, happy paths
                    │ Playwright│             — Login, critical workflows
                    └────┬─────┘
              ┌──────────┴──────────┐
              │     Integration     │  ~100 tests — SQL Server LocalDB / dev instance
              │   (real SQL Server) │             — Full handler stack cu DB real
              └──────────┬──────────┘
         ┌───────────────┴────────────────┐
         │           Unit Tests           │  ~500 tests — Domain + Application
         │   (Domain + Application layer) │             — Pure logic, no IO
         └────────────────────────────────┘
```

### Integration Test Pattern (SQL Server LocalDB)

```csharp
// Finance.Integration.Tests/InvoiceTests.cs
public class CreateInvoiceTests : IClassFixture<FinanceModuleFixture>
{
    private readonly FinanceModuleFixture _fixture;

    [Fact]
    public async Task CreateInvoice_ValidRequest_ReturnsCreatedInvoice()
    {
        // Arrange — SQL Server LocalDB, DbUp rulează migrările automat în fixture
        var command = new CreateInvoiceCommand(
            CustomerId: _fixture.SeedData.CustomerId,
            Lines: [...]);

        // Act
        var result = await _fixture.Sender.Send(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var invoice = await _fixture.InvoiceRepository.GetByIdAsync(result.Value!);
        invoice.Should().NotBeNull();
        invoice!.Status.Should().Be(InvoiceStatus.Draft);
    }
}

// Fixture — folosește LocalDB, DB nou per test class, DbUp aplică migrările
public class FinanceModuleFixture : IAsyncLifetime
{
    private readonly string _dbName = $"ErpTest_{Guid.NewGuid():N}";

    public string ConnectionString =>
        $"Server=(localdb)\\mssqllocaldb;Database={_dbName};Trusted_Connection=True;";

    public async Task InitializeAsync()
    {
        // DbUp creează și migrează DB-ul de test
        MigrationRunner.Run(ConnectionString);
        // Seed date de test
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        // Drop DB după fiecare test class
        using var conn = new SqlConnection(
            "Server=(localdb)\\mssqllocaldb;Trusted_Connection=True;");
        await conn.ExecuteAsync($"DROP DATABASE IF EXISTS [{_dbName}]");
    }
}
```

### Frontend Tests (Vitest + Playwright)

```typescript
// Unit: feature logic
// features/finance/invoices/invoices.test.ts
describe('invoice calculations', () => {
  it('calculates total with VAT correctly', () => {
    const total = calculateInvoiceTotal(lines, vatRate: 0.19);
    expect(total).toBe(expectedTotal);
  });
});

// E2E: Playwright
// tests/e2e/finance/create-invoice.spec.ts
test('creates invoice and navigates to detail', async ({ page }) => {
  await loginAs(page, 'finance.manager');
  await page.goto('/finance/invoices/new');
  await fillInvoiceForm(page, testInvoiceData);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/\/finance\/invoices\/.+/);
});
```

---

## CI/CD & Deployment

### GitHub Actions — CI Pipeline

```yaml
# .github/workflows/ci.yml
name: CI

on: [push, pull_request]

jobs:
  backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.x' }
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
      - run: dotnet test --no-build -c Release --collect:"XPlat Code Coverage"
      - run: dotnet format --verify-no-changes  # Enforce formatting

  frontend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '22' }
      - run: npm ci
      - run: npm run type-check
      - run: npm run lint
      - run: npm run test:unit
      - run: npm run build

  integration-tests:
    runs-on: windows-latest
    needs: [backend]
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.x' }
      - run: dotnet test tests/Integration/ --no-build -c Release
      # LocalDB disponibil nativ pe windows-latest runner (SQL Server LocalDB preinstalat)
```

### Deployment

Aplicația rulează direct pe Windows Server (IIS sau Windows Service) — fără containerizare.

```
Backend:   Publicat ca self-contained executable (.NET 10) sau hosted în IIS
           dotnet publish -c Release -r win-x64 --self-contained

Frontend:  Build static (npm run build → dist/) servit de IIS sau NGINX for Windows
           npx vite build

DB:        SQL Server 2025 instalat nativ, migrările DbUp rulează la fiecare deploy
```

**IIS setup** — `web.config` generat automat de `dotnet publish`. Reverse proxy IIS → Kestrel cu `AspNetCoreModule`.

**Windows Service** (alternativă fără IIS):
```bash
dotnet publish -c Release -r win-x64 --self-contained -o C:\Services\ERP
sc create ERP-Api binPath="C:\Services\ERP\Api.exe" start=auto
sc start ERP-Api
```

---

## Development Setup

### Prerequisites

```bash
# Required
dotnet --version        # 10.x
node --version          # 22.x (LTS)
# SQL Server 2025 instalat nativ (sau LocalDB pentru development/teste)

# Install tools globally
dotnet tool install -g dbup-cli
dotnet tool install -g csharpier    # Opinionated C# formatter
npm install -g @openapi-typescript
```

### First Run

```bash
# 1. Start backend (migrările DbUp rulează automat la startup)
cd src/Api
dotnet run

# 2. Generate API client from OpenAPI spec
npx openapi-typescript http://localhost:5000/openapi/v1.json -o frontend/src/api/generated/api.ts

# 3. Start frontend
cd frontend
npm install
npm run dev
```

### Connection String Security

Connection string-ul **nu apare niciodată** în codul sursă sau în fișiere comise în repo.

```
Development:  dotnet user-secrets (Secret Manager) — stocate local, în afara repo-ului
CI/CD:        GitHub Actions Secrets — injectate ca environment variables la build
Production:   Windows Environment Variables sau fișier criptat cu DPAPI
              Niciodată în appsettings.json / appsettings.Production.json comis în repo
```

```bash
# Setup development (rulat o singură dată per developer)
cd src/Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Server=.;Database=ErpDev;Trusted_Connection=True;"
dotnet user-secrets set "Jwt:SecretKey" "dev-secret-min-32-chars-long-here"
dotnet user-secrets set "Redis" "localhost:6379"
```

```csharp
// appsettings.json — comis în repo, FĂRĂ valori sensibile
{
  "ConnectionStrings": {
    "Default": ""           // valoarea vine din user-secrets / env var
  },
  "Jwt": {
    "SecretKey": "",        // valoarea vine din user-secrets / env var
    "Issuer": "erp-api",
    "Audience": "erp-frontend",
    "ExpiryMinutes": 60
  },
  "Hangfire": { "Dashboard": { "Enabled": false } },
  "Seq": { "ServerUrl": "http://localhost:5341" }
}
```

```csharp
// Program.cs — ordinea de încărcare a configurației
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{env}.json", optional: true)
    .AddUserSecrets<Program>(optional: true)   // development
    .AddEnvironmentVariables();                // production override
```

`.gitignore` obligatoriu:
```
**/appsettings.Development.json
**/appsettings.Production.json
**/*.user
**/secrets.json
```

---

## Conventions & Standards

### Naming — SQL

| Obiect SQL | Convenție | Exemplu |
|---|---|---|
| Schemă | `lowercase` | `finance`, `hr`, `inventory` |
| Tabel | `snake_case` | `invoices`, `invoice_lines` |
| Coloană | `snake_case` | `tenant_id`, `created_at`, `unit_price` |
| Stored Procedure | `usp_{Action}{Entity}` | `usp_CreateInvoice` |
| View | `vw_{Description}` | `vw_InvoiceAging` |
| Table-Valued Function | `tvf_{Description}` | `tvf_InvoiceLines` |
| Scalar Function | `fn_{Description}` | `fn_CalculateVAT` |
| Primary Key | `PK_{TableName}` | `PK_invoices` |
| Foreign Key | `FK_{Table}_{ReferencedTable}` | `FK_invoice_lines_invoices` |
| Unique Constraint | `UQ_{Table}_{Columns}` | `UQ_invoices_tenant_number` |
| Index | `IX_{Table}_{Columns}` | `IX_invoices_tenant_status` |
| Trigger | `trg_{Table}_{Timing}_{Action}` | `trg_invoices_After_Update` |
| Migration script | `YYYYMMDD_NNN_Description.sql` | `20260516_001_CreateInvoicesTable.sql` |

### Naming — C#

| Artifact | Convenție | Exemplu |
|---|---|---|
| Class, Record, Struct | `PascalCase` | `InvoiceRepository` |
| Interface | `IPascalCase` | `IInvoiceRepository` |
| Method | `PascalCase` | `GetByIdAsync` |
| Property | `PascalCase` | `TenantId`, `CreatedAt` |
| Private field | `_camelCase` | `_connectionFactory`, `_tenant` |
| Parameter | `camelCase` | `tenantId`, `cancellationToken` |
| Local variable | `camelCase` | `invoiceId`, `result` |
| Constant | `PascalCase` | `MaxPageSize` |
| Command | `{Verb}{Entity}Command` | `CreateInvoiceCommand` |
| Query | `{Get\|List}{Entity}Query` | `GetInvoiceByIdQuery` |
| Handler | `{CommandOrQuery}Handler` | `CreateInvoiceCommandHandler` |
| Validator | `{CommandOrQuery}Validator` | `CreateInvoiceCommandValidator` |
| Controller | `{Entity}Controller` | `InvoiceController` |
| Repository interface | `I{Entity}Repository` | `IInvoiceRepository` |
| Domain Event | `{Entity}{PastTense}DomainEvent` | `InvoiceApprovedDomainEvent` |
| Integration Event | `{Entity}{PastTense}IntegrationEvent` | `InvoicePaidIntegrationEvent` |
| DTO (read) | `{Entity}{Detail\|List}Dto` | `InvoiceDetailDto`, `InvoiceListDto` |
| Request (API input) | `{Action}{Entity}Request` | `CreateInvoiceRequest` |
| Response (API output) | `{Action}{Entity}Response` | `CreateInvoiceResponse` |
| Error class | `{Module}Errors` | `FinanceErrors` |

### Naming — TypeScript / React

| Artifact | Convenție | Exemplu |
|---|---|---|
| Component | `PascalCase` | `InvoiceList`, `InvoiceForm` |
| Hook | `useCamelCase` | `useInvoices`, `usePermission` |
| Store (Zustand) | `use{Name}Store` | `useAuthStore` |
| Fișier component | `PascalCase.tsx` | `InvoiceList.tsx` |
| Fișier utilitar | `kebab-case.ts` | `format-currency.ts` |
| Fișier API hooks | `api.ts` (per feature) | `features/finance/invoices/api.ts` |
| Fișier scheme | `schemas.ts` (per feature) | `features/finance/invoices/schemas.ts` |
| Interface / Type | `PascalCase` | `InvoiceDetailDto`, `CreateInvoiceRequest` |
| Variabilă / funcție | `camelCase` | `invoiceId`, `handleSubmit` |
| Constantă globală | `UPPER_SNAKE_CASE` | `MAX_PAGE_SIZE` |
| Query keys object | `{entity}Keys` | `invoiceKeys` |
| Enum | `PascalCase` | `InvoiceStatus` |
| Enum valori | `PascalCase` | `InvoiceStatus.Draft` |

### Reguli absolute — SQL

```
✅ Tot SQL-ul trăiește în fișiere .sql versionabile în repo
✅ Aplicația apelează EXCLUSIV: Stored Procedures, Views, TVF, Scalar Functions
✅ CREATE OR ALTER PROCEDURE — toate SP-urile sunt idempotente
✅ tenant_id = @TenantId în ORICE SP care accesează date
✅ Parametri @NumeParametru — niciodată concatenare de string-uri în SQL
✅ Coloane explicite în SELECT — niciodată SELECT *
✅ Scheme explicite — niciodată dbo, întotdeauna finance., hr., etc.
✅ Toate obiectele SQL au schema prefix

🚫 INTERZIS: SQL inline în C# (const string sql = "SELECT...")
🚫 INTERZIS: LINQ to SQL sau orice ORM care generează SQL
🚫 INTERZIS: SELECT * în orice obiect SQL
🚫 INTERZIS: Concatenare string în SQL (SQL injection)
🚫 INTERZIS: NEWID() ca DEFAULT pe coloane PK
🚫 INTERZIS: Obiecte SQL fără schema prefix
```

### Reguli absolute — C#

```
✅ Result<T> pentru orice operație care poate eșua — nu throw pentru erori business
✅ Records imutabile pentru Commands și Queries
✅ sealed pe orice clasă care nu e destinată moștenirii
✅ CancellationToken pe orice metodă async publică
✅ using var conn — conexiunile se închid automat
✅ CommandDefinition cu cancellationToken la fiecare apel Dapper
✅ Dependency injection prin constructor — niciodată Service Locator

🚫 INTERZIS: SQL inline în C# — orice string cu SELECT/INSERT/UPDATE/DELETE
🚫 INTERZIS: EF Core (DbContext, DbSet, Include, Migration EF)
🚫 INTERZIS: Connection string hardcodat sau în appsettings.json comis
🚫 INTERZIS: async void (excepție: event handlers UI)
🚫 INTERZIS: throw pentru erori business — folosește Result<T>
🚫 INTERZIS: logică business în Controller — doar mapare + ISender.Send()
🚫 INTERZIS: referință directă între module
```

### Reguli absolute — TypeScript

```
✅ strict: true în tsconfig — fără excepții
✅ Tipuri API din clientul generat (openapi-typescript) — niciodată scrise manual
✅ Zod pentru validare forme — schema în schemas.ts separat de componentă
✅ TanStack Query pentru orice date din API — niciodată useState + useEffect + fetch
✅ Return type explicit pe orice funcție exportată

🚫 INTERZIS: any — folosește unknown și narrowing
🚫 INTERZIS: barrel files (index.ts cu re-exporturi)
🚫 INTERZIS: fetch direct în componentă — trece prin TanStack Query
🚫 INTERZIS: tipuri API scrise manual — vin din openapi-typescript
🚫 INTERZIS: logică business în componentă — extrage în hook sau utilitar
```

### C# Specific

```csharp
// Commands și Queries — records imutabile, sealed
public sealed record CreateInvoiceCommand(
    Guid CustomerId,
    DateOnly DueDate,
    IReadOnlyList<InvoiceLineDto> Lines) : ICommand<Result<Guid>>;

// Repository — apelează SP, zero SQL inline
public async Task<InvoiceDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
{
    using var conn = _connectionFactory.Create();
    return await conn.QuerySingleOrDefaultAsync<InvoiceDetailDto>(
        new CommandDefinition(
            "usp_GetInvoiceById",
            new { Id = id, TenantId = _tenant.TenantId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
}
```

### TypeScript Specific

```typescript
// Strict mode, no any, explicit return types pe funcții exportate
// Tipuri API din clientul generat — niciodată scrise manual
// Zod pentru validare forme — schema în schemas.ts
// No barrel files
```


---

## Architecture Decision Records

### ADR-001: Modular Monolith over Microservices

**Status**: Accepted  
**Context**: ERP requires cross-domain transactions (Finance ↔ Inventory ↔ HR) and a small team.  
**Decision**: Start as a modular monolith. Modules are isolated via schemas and explicit contracts. Extract to service only when a module has independent scaling requirements or a separate team.  
**Consequences**: Simpler deployment, no distributed tracing complexity, true ACID transactions across modules.

### ADR-002: Dapper + Stored Procedures — zero SQL inline în C#

**Status**: Accepted  
**Context**: ERP requires complex reporting queries, CTEs, window functions, and tight performance control. SQL inline în C# este imposibil de refactorizat, de testat izolat, și de versionat coerent.  
**Decision**: Dapper apelează exclusiv obiecte SQL (stored procedures, views, table-valued functions). Zero string-uri SQL în cod C#. DbUp pentru migrări de schemă (DDL). Stored procedures într-un folder dedicat, aplicate ca „always run" scripts (idempotente, `CREATE OR ALTER`).  
**Consequences**: SQL trăiește în fișiere `.sql` versionabile, testabile, și executabile direct din SSMS. Devii C# nu scriu SQL — doar apelează obiecte SQL cu parametri. Non-negociabil.

### ADR-003: Vertical Slice Architecture within Clean Architecture

**Status**: Accepted  
**Context**: Feature teams need to work without stepping on each other. CRUD-per-layer architecture causes merge conflicts and bloated services.  
**Decision**: Each feature is a vertical slice: endpoint → command/query → handler → repository → SQL, all in one folder. Shared domain primitives live in `Shared.Kernel`. Cross-module concerns live in `Shared.Infrastructure`.  
**Consequences**: High cohesion per feature, low coupling between features. Slight duplication acceptable (prefer duplication over wrong abstraction).

### ADR-004: Multi-Tenancy from Day 1

**Status**: Accepted  
**Context**: ERP is a SaaS platform. Adding multi-tenancy after the fact is painful and risky.  
**Decision**: Every table has `tenant_id`. Every repository method receives and filters by `TenantContext`. Tenant isolation is verified in integration tests.  
**Consequences**: Slightly more complex queries. `TenantId` must be on every index. Row-level security as defense-in-depth (optional but recommended).

### ADR-005: Hybrid Primary Key Strategy (UUIDv7 + BIGINT IDENTITY)

**Status**: Accepted  
**Context**: Un ERP are două categorii distincte de tabele cu cerințe opuse: aggregate roots care trebuie identificate înainte de INSERT (pentru Domain Events și API URLs), și tabele de volum mare (child tables, audit, log) unde performance-ul de insert și join contează mai mult decât orice altceva.  
**Decision**:  
- Aggregate roots → `UNIQUEIDENTIFIER` generat în C# cu UUIDv7 (`UUIDNext`). Time-ordered, aproape secvențial, zero fragmentation problematică pe clustered index.  
- Child tables, line tables, audit, append-only → `BIGINT IDENTITY`. Insert secvențial pur, 8 bytes vs 16, join performance maxim.  
- Exclus `NEWID()` (random GUID = page splits garantate). Exclus `NEWSEQUENTIALID()` (generat de SQL Server, ID nedisponibil în C# înainte de INSERT).  

**Consequences**: ID-ul unui aggregate root există în C# înainte de INSERT — Domain Events pot fi publicate cu ID-ul corect fără roundtrip la DB. Child tables rămân compacte și rapide. Devii trebuie să știe în ce categorie cade fiecare tabel nou.

---

*Last updated: May 2026*  
*Architecture Owner: Aurelian*
