---
mode: agent
description: Generează un fișier de migrare DbUp corect — tabel nou, coloană, index, sau stored procedure.
---

Creează un fișier de migrare DbUp respectând skill-ul `new-migration`.

**Informații necesare:**
- Tip: [CREATE TABLE / ALTER TABLE / CREATE INDEX / CREATE SCHEMA / Stored Procedure]
- Modul și schemă: [finance / hr / inventory / ...]
- Descriere obiect: [ex: tabel invoices, coloană notes pe invoices, index pe status]

**Reguli obligatorii pentru migrări DDL (`Migrations/`):**
- Naming: `YYYYMMDD_NNN_Description.sql` (data de azi)
- `tenant_id UNIQUEIDENTIFIER NOT NULL` pe orice tabel nou
- `tenant_id` = prima coloană în orice index
- `created_at DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME()` + `created_by`
- Aggregate roots: PK `UNIQUEIDENTIFIER NOT NULL` (ID din C#, nu DEFAULT)
- Child tables: PK `BIGINT IDENTITY(1,1) NOT NULL`
- Constraint-uri cu nume explicit: `PK_`, `FK_`, `UQ_`, `IX_`
- INTERZIS: `DEFAULT NEWID()` sau `DEFAULT NEWSEQUENTIALID()` pe PK

**Reguli obligatorii pentru Stored Procedures (`StoredProcedures/`):**
- `CREATE OR ALTER PROCEDURE {schema}.usp_{Action}{Entity}`
- `SET NOCOUNT ON`
- `@TenantId UNIQUEIDENTIFIER` ca parametru
- `AND tenant_id = @TenantId` în WHERE și pe fiecare JOIN
- Coloane explicite în SELECT — niciodată `SELECT *`
- Schema prefix obligatoriu — niciodată `dbo`
