# GitHub Copilot Instructions — ERP Platform

## Stack
- **Backend**: .NET 10, ASP.NET Core Controllers, MediatR, Dapper, FluentValidation, DbUp, Hangfire, Serilog, UUIDNext
- **Frontend**: React 19, TypeScript (strict), Vite, TanStack Query/Router, Zustand, React Hook Form, Zod, shadcn/ui, Tailwind CSS 4, Syncfusion Grid (Community Edition)
- **Database**: SQL Server 2025 — Dapper apelează exclusiv SP/View/TVF, zero SQL inline în C#
- **No Docker** — deployment direct pe Windows Server (IIS / Windows Service)

## Architecture
- **Pattern**: Modular Monolith + Clean Architecture + Vertical Slice Architecture (VSA)
- Fiecare feature: `Controller Action → Command/Query → Handler → Repository → SP apelat cu Dapper`
- Module comunică exclusiv prin `IntegrationEvents` din `Shared.Contracts` — zero referință directă
- Dependency rule: `Domain` ← `Application` ← `Infrastructure` ← `Api`

---

## Naming Conventions — SQL

| Obiect | Convenție | Exemplu |
|---|---|---|
| Schemă | `lowercase` | `finance`, `hr` |
| Tabel | `snake_case` | `invoices`, `invoice_lines` |
| Coloană | `snake_case` | `tenant_id`, `unit_price` |
| Stored Procedure | `usp_{Action}{Entity}` | `usp_CreateInvoice` |
| View | `vw_{Description}` | `vw_InvoiceAging` |
| Table-Valued Function | `tvf_{Description}` | `tvf_InvoiceLines` |
| Scalar Function | `fn_{Description}` | `fn_CalculateVAT` |
| PK | `PK_{TableName}` | `PK_invoices` |
| FK | `FK_{Table}_{ReferencedTable}` | `FK_invoice_lines_invoices` |
| Unique | `UQ_{Table}_{Columns}` | `UQ_invoices_tenant_number` |
| Index | `IX_{Table}_{Columns}` | `IX_invoices_tenant_status` |
| Migration | `YYYYMMDD_NNN_Description.sql` | `20260516_001_CreateInvoicesTable.sql` |

## Naming Conventions — C#

| Artifact | Convenție | Exemplu |
|---|---|---|
| Class / Record / Struct | `PascalCase` | `InvoiceRepository` |
| Interface | `IPascalCase` | `IInvoiceRepository` |
| Method | `PascalCase` | `GetByIdAsync` |
| Property | `PascalCase` | `TenantId`, `CreatedAt` |
| Private field | `_camelCase` | `_connectionFactory` |
| Parameter / Local var | `camelCase` | `tenantId`, `result` |
| Command | `{Verb}{Entity}Command` | `CreateInvoiceCommand` |
| Query | `{Get/List}{Entity}Query` | `GetInvoiceByIdQuery` |
| Handler | `{CommandOrQuery}Handler` | `CreateInvoiceCommandHandler` |
| Validator | `{CommandOrQuery}Validator` | `CreateInvoiceCommandValidator` |
| Controller | `{Entity}Controller` | `InvoiceController` |
| Domain Event | `{Entity}{PastTense}DomainEvent` | `InvoiceApprovedDomainEvent` |
| Integration Event | `{Entity}{PastTense}IntegrationEvent` | `InvoicePaidIntegrationEvent` |
| DTO | `{Entity}{Detail/List}Dto` | `InvoiceDetailDto` |
| Request | `{Action}{Entity}Request` | `CreateInvoiceRequest` |
| Error class | `{Module}Errors` | `FinanceErrors` |

## Naming Conventions — TypeScript / React

| Artifact | Convenție | Exemplu |
|---|---|---|
| Component | `PascalCase.tsx` | `InvoiceList.tsx` |
| Hook | `useCamelCase` | `useInvoices` |
| Store Zustand | `use{Name}Store` | `useAuthStore` |
| Fișier utilitar | `kebab-case.ts` | `format-currency.ts` |
| Interface / Type | `PascalCase` | `InvoiceDetailDto` |
| Variabilă / funcție | `camelCase` | `handleSubmit` |
| Constantă globală | `UPPER_SNAKE_CASE` | `MAX_PAGE_SIZE` |
| Query keys | `{entity}Keys` | `invoiceKeys` |
| Enum + valori | `PascalCase` | `InvoiceStatus.Draft` |

---

## Reguli absolute — SQL

```
PERMIS:    Tot SQL-ul trăiește în fișiere .sql versionabile
PERMIS:    Aplicația apelează EXCLUSIV: Stored Procedures, Views, TVF, Scalar Functions
PERMIS:    CREATE OR ALTER PROCEDURE — toate SP-urile sunt idempotente
PERMIS:    tenant_id = @TenantId în ORICE SP care accesează date
PERMIS:    Parametri @NumeParametru — niciodată concatenare de string-uri
PERMIS:    SELECT cu coloane explicite — niciodată SELECT *
PERMIS:    Schema prefix obligatoriu — niciodată dbo

INTERZIS:  SQL inline în C# (const string sql = "SELECT...")
INTERZIS:  SELECT * în orice obiect SQL
INTERZIS:  Concatenare string în SQL
INTERZIS:  NEWID() ca DEFAULT pe PK
INTERZIS:  Obiecte SQL fără schema prefix
```

## Reguli absolute — C#

```
PERMIS:    Result<T> pentru orice operație care poate eșua
PERMIS:    Records imutabile și sealed pentru Commands și Queries
PERMIS:    CancellationToken pe orice metodă async publică
PERMIS:    using var conn — conexiunile se închid automat
PERMIS:    CommandDefinition cu cancellationToken la fiecare apel Dapper
PERMIS:    Connection string EXCLUSIV din user-secrets / environment variables

INTERZIS:  SQL inline în C# — orice string cu SELECT/INSERT/UPDATE/DELETE
INTERZIS:  EF Core (DbContext, DbSet, Include)
INTERZIS:  Connection string hardcodat sau în fișiere comise în repo
INTERZIS:  throw pentru erori business — folosește Result<T>
INTERZIS:  Logică business în Controller
INTERZIS:  Referință directă între module
INTERZIS:  Minimal APIs în loc de Controllers cu [ApiController]
```

## Reguli absolute — TypeScript

```
PERMIS:    strict: true în tsconfig
PERMIS:    Tipuri API din openapi-typescript — niciodată scrise manual
PERMIS:    Zod pentru validare forme, schema în schemas.ts separat
PERMIS:    TanStack Query pentru orice date din API
PERMIS:    Return type explicit pe funcții exportate

INTERZIS:  any
INTERZIS:  Barrel files (index.ts cu re-exporturi)
INTERZIS:  fetch direct în componentă
INTERZIS:  Tipuri API scrise manual

TanStack Router — file-based routing:
PERMIS:    Componenta din route file se numește ÎNTOTDEAUNA RouteComponent
PERMIS:    Route export PRIMUL, funcția RouteComponent după
PERMIS:    Logica reală a paginii în fișier separat cu prefix "-" (ex. -page.tsx) — ignorat de router
INTERZIS:  Nume custom pentru componenta din route file (ex. InvoicePage, DashboardPage)
INTERZIS:  JSX/logică proprie direct în RouteComponent din index.tsx
MOTIV:     @tanstack/router-generator suprascrie fișierele de rută la prima rulare (Vite start / tsr watch)
           când fișierul nu se află în cache-ul intern al generatorului.
           Generatorul înlocuiește ÎNTREGUL conținut cu template-ul Hello"%%tsrPath%%",
           indiferent de ce există deja în fișier.

PATTERN CORECT — structura unui route folder:
   routes/_auth/finance/invoices/
     ├── index.tsx       ← gestionat de generator (nu pune JSX propriu aici!)
     └── -page.tsx       ← ignorat de router, conține logica reală

   // index.tsx — DOAR bridge spre -page.tsx
   import { createFileRoute } from '@tanstack/react-router'
   import InvoicesPage from './-page'

   export const Route = createFileRoute('/_auth/finance/invoices/')({
     component: RouteComponent,
   })

   function RouteComponent() {
     return <InvoicesPage />
   }

   // -page.tsx — logica reală, fișier normal React
   export default function InvoicesPage() {
     return <div>...</div>
   }

EXCEPȚIE:   Fișierele deja existente în cache-ul generatorului (create înainte de prima rulare
            a watcher-ului) pot conține JSX direct în RouteComponent fără probleme.
            Fișierele NOI create după ce Vite/tsr watch rulează → folosește întotdeauna -page.tsx.
```

---

## Patterns — cod de referință

### Repository — apelează SP, zero SQL inline

```csharp
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

### Handler — Result<T>, fără throw business

```csharp
public async Task<Result<Guid>> Handle(CreateInvoiceCommand cmd, CancellationToken ct)
{
    var customer = await _repo.GetCustomerAsync(cmd.CustomerId, ct);
    if (customer is null)
        return Result<Guid>.Failure(FinanceErrors.CustomerNotFound(cmd.CustomerId));

    var invoice = Invoice.Create(cmd.CustomerId, cmd.Lines);
    await _repo.InsertAsync(invoice, ct);
    return Result<Guid>.Success(invoice.Id);
}
```

### Primary Key Strategy

| Tabel | PK |
|---|---|
| Aggregate roots (Invoice, Employee…) | `UNIQUEIDENTIFIER` — UUIDv7 din C# |
| Child/line tables, audit, log | `BIGINT IDENTITY` |

### MediatR Pipeline (ordine fixă)
`Logging → Validation → Authorization → Caching → Transaction → Audit`

### Frontend — Query keys

```typescript
export const invoiceKeys = {
  all: ['invoices'] as const,
  list: (f: InvoiceFilters) => [...invoiceKeys.all, 'list', f] as const,
  detail: (id: string) => [...invoiceKeys.all, 'detail', id] as const,
};
```
