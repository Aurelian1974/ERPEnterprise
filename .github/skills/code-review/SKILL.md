---
name: code-review
description: >-
  Checklist de code review pentru ERP: verifică SQL inline în C# (blocker),
  connection string expus (blocker), naming conventions SQL/C#/TS,
  tenant_id, Result<T>, arhitectură VSA, reguli frontend.
---

# Code Review

## Când se aplică
Când utilizatorul cere review la cod, verificare PR, sau validare că un
feature respectă convențiile proiectului.

---

## 🔴 BLOCKERS — respins imediat, indiferent de altceva

```
SQL INLINE ÎN C#
  Orice string cu SELECT, INSERT, UPDATE, DELETE, EXEC în cod C#
  const string sql = "SELECT..."  ← BLOCKER
  var sql = $"SELECT...{variable}..." ← BLOCKER + SQL injection

CONNECTION STRING EXPUS
  Orice connection string în: appsettings.json comis, appsettings.Production.json,
  orice fișier C# sau TS, orice fișier comis în repo
  "Server=.;Database=ERP;Password=..." ← BLOCKER

EF CORE
  Orice import sau utilizare de: DbContext, DbSet, Include,
  Migration EF, IQueryable pe entități DB
  ← BLOCKER

REFERINȚĂ DIRECTĂ ÎNTRE MODULE
  using Finance.Domain în HR.Application ← BLOCKER
  ProjectReference cross-modul ← BLOCKER

NEWID() PE PK
  DEFAULT NEWID() sau DEFAULT NEWSEQUENTIALID() pe coloane PK ← BLOCKER
```

---

## Checklist SQL

### Obiecte SQL
- [ ] Tot SQL-ul trăiește în fișiere `.sql` în `Database/Migrations/` sau `Database/StoredProcedures/`
- [ ] SP-uri: `CREATE OR ALTER PROCEDURE` — niciodată `CREATE PROCEDURE` simplu
- [ ] Niciun `SELECT *` în SP, View, sau TVF — coloane explicite întotdeauna
- [ ] Schema prefix pe ORICE obiect SQL (`finance.invoices`, nu `dbo.invoices`)
- [ ] Niciodată concatenare string în SQL — parametri `@NumeParametru` întotdeauna

### Naming SQL
- [ ] Schemă: `lowercase` (`finance`, `hr`, `inventory`)
- [ ] Tabel: `snake_case` (`invoices`, `invoice_lines`)
- [ ] Coloană: `snake_case` (`tenant_id`, `unit_price`, `created_at`)
- [ ] SP: `usp_{Action}{Entity}` (`usp_CreateInvoice`)
- [ ] View: `vw_{Description}` (`vw_InvoiceAging`)
- [ ] TVF: `tvf_{Description}` (`tvf_InvoiceLines`)
- [ ] PK: `PK_{TableName}`, FK: `FK_{Table}_{Ref}`, Index: `IX_{Table}_{Columns}`
- [ ] Migration: `YYYYMMDD_NNN_Description.sql`

### Multi-Tenancy
- [ ] `tenant_id UNIQUEIDENTIFIER NOT NULL` pe ORICE tabel nou
- [ ] `AND tenant_id = @TenantId` în ORICE SP/Query care accesează date
- [ ] `tenant_id` = PRIMA coloană în orice index composite

### Primary Keys
- [ ] Aggregate roots: `UNIQUEIDENTIFIER NOT NULL` — ID vine din C# (UUIDv7)
- [ ] Child/line tables + audit/log: `BIGINT IDENTITY(1,1) NOT NULL`

---

## Checklist C#

### Arhitectură
- [ ] Feature organizat ca vertical slice (`Features/{Entity}/{Action}/`)
- [ ] Handler nu conține referință la alt modul
- [ ] `Domain` project: zero NuGet packages externe, zero referințe la Infrastructure
- [ ] Controller: zero logică business — doar mapare request → command → result → response

### Repository
- [ ] ZERO SQL inline — repository apelează exclusiv SP/View/TVF
- [ ] `CommandDefinition` cu `commandType: CommandType.StoredProcedure` pe fiecare apel
- [ ] `using var conn = _connectionFactory.Create()` pe fiecare metodă
- [ ] `CancellationToken` transmis prin `CommandDefinition`

### Result Pattern
- [ ] Handler returnează `Result<T>` — niciodată `throw` pentru erori business
- [ ] Erorile definite în `{Module}Errors` static class — nu strings hardcodate
- [ ] Controller verifică `result.IsSuccess` înainte de răspuns

### Security
- [ ] Connection string absent din cod și fișiere comise — vine din user-secrets / env vars
- [ ] `.gitignore` conține `appsettings.Development.json` și `appsettings.Production.json`

### Naming C#
- [ ] Class/Record/Struct: `PascalCase`, Interface: `IPascalCase`
- [ ] Private fields: `_camelCase`, Properties: `PascalCase`
- [ ] Commands: `{Verb}{Entity}Command`, Queries: `{Get|List}{Entity}Query`
- [ ] Handlers: sufix `Handler`, Validators: sufix `Validator`
- [ ] Controllers: `{Entity}Controller` cu `[ApiController]`
- [ ] DTO: `{Entity}{Detail|List}Dto`, Request: `{Action}{Entity}Request`

### Controller
- [ ] `[ApiController]` pe controller
- [ ] `[Authorize(Policy = "{module}.{entity}.{action}")]` pe fiecare action
- [ ] `[ProducesResponseType]` declarat pentru fiecare status code
- [ ] Niciodată Minimal API endpoint în loc de Controller action

### Async
- [ ] `CancellationToken` pe fiecare metodă async publică
- [ ] Niciodată `async void` (excepție: event handlers UI)
- [ ] Niciodată `.Result` sau `.Wait()` pe Task — riscă deadlock

---

## Checklist TypeScript / React

### Structură
- [ ] Feature în `features/{module}/{entity}/` — nu cod feature-specific în `components/`
- [ ] Niciodată barrel files (`index.ts` cu re-exporturi)
- [ ] Tipuri API din clientul generat (`api/generated/`) — niciodată scrise manual

### Data Fetching
- [ ] TanStack Query pentru orice date server — niciodată `useState + useEffect + fetch`
- [ ] Query keys în `{entity}Keys` din `api.ts` — nu strings inline
- [ ] `queryClient.invalidateQueries` după mutații — nu refetch manual

### Forme
- [ ] React Hook Form + Zod — nu forme controlled cu `useState` per câmp
- [ ] Schema Zod în `schemas.ts` separat — nu inline în componentă

### TypeScript
- [ ] `strict: true` în tsconfig verificat
- [ ] Zero utilizări de `any` — `unknown` cu narrowing dacă e necesar
- [ ] Return type explicit pe funcții exportate

### Security
- [ ] Niciodată date sensibile în `localStorage`
- [ ] JWT în memory store sau httpOnly cookie — nu localStorage

---

## Sumar rapid

| Categorie | Cel mai frecvent de ratat |
|---|---|
| SQL | `const string sql = "SELECT..."` în C# |
| Security | Connection string în appsettings.json comis |
| Multi-tenancy | SP fără `AND tenant_id = @TenantId` |
| C# | `throw new Exception(...)` în loc de `Result<T>` |
| SQL Objects | `CREATE PROCEDURE` în loc de `CREATE OR ALTER` |
| Naming | SP fără `usp_{Action}{Entity}` |
| Frontend | `any` sau barrel `index.ts` |
