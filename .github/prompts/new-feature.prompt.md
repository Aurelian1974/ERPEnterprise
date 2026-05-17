---
mode: agent
description: Scaffolează un feature VSA complet — Command/Query, Handler, Validator, Controller, Repository, SP SQL.
---

Creează un feature vertical slice complet pentru ERP respectând skill-ul `new-vertical-slice`.

**Informații necesare:**
- Modul: [Finance / HR / Inventory / Purchasing / Sales / Administration]
- Entitate: [ex: Invoice, Employee, Product]
- Acțiune: [Create / Update / Delete / GetById / List / Approve / ...]
- Câmpuri principale ale comenzii/query-ului

**Generează în ordine:**
1. `{Action}{Entity}Command.cs` sau `{Action}{Entity}Query.cs`
2. `{Action}{Entity}CommandHandler.cs` cu `Result<T>`
3. `{Action}{Entity}Validator.cs` cu FluentValidation
4. `{Action}{Entity}Request.cs` și `{Action}{Entity}Response.cs`
5. SP SQL: `usp_{Action}{Entity}.sql` cu `CREATE OR ALTER`, `tenant_id`, `SET NOCOUNT ON`
6. Metodă repository care apelează SP-ul (zero SQL inline în C#)
7. Controller action cu `[Authorize(Policy = "{module}.{entity}.{action}")]`

**Reguli obligatorii:**
- Zero SQL inline în C# — exclusiv apeluri SP prin Dapper
- `Result<T>` în handler — niciodată `throw` pentru erori business
- `tenant_id = @TenantId` în SP
- ID aggregate root = UUIDv7 din C# (`Uuid.NewDatabaseFriendly`)
- SP: `CREATE OR ALTER`, `SET NOCOUNT ON`, coloane explicite în SELECT
