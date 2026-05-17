---
name: new-migration
description: >-
  Generează fișiere DbUp pentru ERP: migrări DDL (tabele, indexuri, scheme)
  și stored procedures (CREATE OR ALTER, idempotente). Naming convention strict,
  tenant_id obligatoriu, strategie PK hibridă UUIDv7/BIGINT.
---

# New Migration

## Când se aplică
Când utilizatorul cere un tabel nou, coloane noi, indexuri, scheme,
sau stored procedures noi / modificate.

## Două tipuri de fișiere SQL — locații diferite

```
src/Modules/{Module}/{Module}.Infrastructure/Database/
│
├── Migrations/          ← DDL versioned — rulat O SINGURĂ DATĂ de DbUp (journaled)
│   └── YYYYMMDD_NNN_Description.sql
│
└── StoredProcedures/    ← DML logic — rulat LA FIECARE DEPLOY de DbUp (always run)
    └── usp_{Action}{Entity}.sql
```

**Migrări**: CREATE TABLE, ALTER TABLE, CREATE INDEX, CREATE SCHEMA — irepetabile.
**Stored Procedures**: CREATE OR ALTER — idempotente, se suprascriu la fiecare deploy.

---

## Naming convention — Migrări DDL

```
YYYYMMDD_NNN_Description.sql

20260516_001_CreateInvoicesTable.sql
20260516_002_AddInvoiceStatusIndex.sql
20260516_003_AddDueDateToInvoices.sql
20260517_001_CreateInvoiceLinesTable.sql
```

- Data = ziua curentă `YYYYMMDD`
- NNN = secvențial per zi, 3 cifre, de la 001
- Description = PascalCase, fără spații

## Naming convention — Obiecte SQL

| Obiect | Convenție | Exemplu |
|---|---|---|
| Schemă | `lowercase` | `finance`, `hr` |
| Tabel | `snake_case` | `invoices`, `invoice_lines` |
| Coloană | `snake_case` | `tenant_id`, `unit_price` |
| Stored Procedure | `usp_{Action}{Entity}` | `usp_CreateInvoice` |
| View | `vw_{Description}` | `vw_InvoiceAging` |
| TVF | `tvf_{Description}` | `tvf_InvoiceLines` |
| PK | `PK_{TableName}` | `PK_invoices` |
| FK | `FK_{Table}_{ReferencedTable}` | `FK_invoice_lines_invoices` |
| Unique | `UQ_{Table}_{Columns}` | `UQ_invoices_tenant_number` |
| Index | `IX_{Table}_{Columns}` | `IX_invoices_tenant_status` |

---

## Strategie PK

| Tabel | PK | Motivul |
|---|---|---|
| Aggregate root (Invoice, Employee…) | `UNIQUEIDENTIFIER NOT NULL` | ID generat în C# (UUIDv7) înainte de INSERT |
| Child / line (InvoiceLines…) | `BIGINT IDENTITY(1,1) NOT NULL` | Volum mare, join performant, niciodată expus |
| Append-only / log / audit | `BIGINT IDENTITY(1,1) NOT NULL` | Insert speed maxim |

**INTERZIS**: `DEFAULT NEWID()` sau `DEFAULT NEWSEQUENTIALID()` pe aggregate roots.

---

## Template — CREATE SCHEMA (primul migration per modul)

```sql
-- 20260516_001_CreateSchemaFinance.sql
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'finance')
    EXEC('CREATE SCHEMA finance');
GO
```

## Template — CREATE TABLE aggregate root

```sql
-- 20260516_002_CreateInvoicesTable.sql
CREATE TABLE finance.invoices (
    id              UNIQUEIDENTIFIER    NOT NULL,
    tenant_id       UNIQUEIDENTIFIER    NOT NULL,
    customer_id     UNIQUEIDENTIFIER    NOT NULL,
    invoice_number  NVARCHAR(50)        NOT NULL,
    status          TINYINT             NOT NULL DEFAULT 1,
    due_date        DATE                NOT NULL,
    total_amount    DECIMAL(18,4)       NOT NULL DEFAULT 0,
    created_at      DATETIME2(7)        NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by      UNIQUEIDENTIFIER    NOT NULL,
    updated_at      DATETIME2(7)        NULL,
    updated_by      UNIQUEIDENTIFIER    NULL,
    CONSTRAINT PK_invoices              PRIMARY KEY CLUSTERED (id),
    CONSTRAINT UQ_invoices_tenant_number UNIQUE (tenant_id, invoice_number)
);
GO

-- tenant_id PRIMA coloană în orice index
CREATE INDEX IX_invoices_tenant_status
    ON finance.invoices (tenant_id, status)
    INCLUDE (invoice_number, due_date, total_amount);
GO

CREATE INDEX IX_invoices_tenant_customer
    ON finance.invoices (tenant_id, customer_id);
GO
```

## Template — CREATE TABLE child / line

```sql
-- 20260516_003_CreateInvoiceLinesTable.sql
CREATE TABLE finance.invoice_lines (
    id              BIGINT IDENTITY(1,1)    NOT NULL,
    invoice_id      UNIQUEIDENTIFIER        NOT NULL,
    tenant_id       UNIQUEIDENTIFIER        NOT NULL,
    product_id      UNIQUEIDENTIFIER        NOT NULL,
    description     NVARCHAR(500)           NOT NULL,
    quantity        DECIMAL(18,4)           NOT NULL,
    unit_price      DECIMAL(18,4)           NOT NULL,
    vat_rate        DECIMAL(5,4)            NOT NULL DEFAULT 0.19,
    line_total      AS (quantity * unit_price) PERSISTED,
    sort_order      INT                     NOT NULL DEFAULT 0,
    CONSTRAINT PK_invoice_lines PRIMARY KEY CLUSTERED (id),
    CONSTRAINT FK_invoice_lines_invoices
        FOREIGN KEY (invoice_id)
        REFERENCES finance.invoices(id) ON DELETE CASCADE
);
GO

CREATE INDEX IX_invoice_lines_invoice_id
    ON finance.invoice_lines (invoice_id)
    INCLUDE (product_id, quantity, unit_price, line_total);
GO
```

## Template — ALTER TABLE (adăugare coloană)

```sql
-- 20260517_001_AddNotesToInvoices.sql
ALTER TABLE finance.invoices
    ADD notes NVARCHAR(1000) NULL;
GO
```

## Template — Stored Procedure (CREATE OR ALTER — întotdeauna)

```sql
-- StoredProcedures/usp_CreateInvoice.sql
CREATE OR ALTER PROCEDURE finance.usp_CreateInvoice
    @Id             UNIQUEIDENTIFIER,
    @TenantId       UNIQUEIDENTIFIER,
    @CustomerId     UNIQUEIDENTIFIER,
    @InvoiceNumber  NVARCHAR(50),
    @DueDate        DATE,
    @CreatedBy      UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO finance.invoices (
        id, tenant_id, customer_id, invoice_number,
        due_date, created_at, created_by
    )
    VALUES (
        @Id, @TenantId, @CustomerId, @InvoiceNumber,
        @DueDate, SYSUTCDATETIME(), @CreatedBy
    );
END;
GO
```

## Reguli obligatorii
- `tenant_id UNIQUEIDENTIFIER NOT NULL` pe ORICE tabel nou
- `tenant_id` = PRIMA coloană în orice index composite
- `created_at` și `created_by` pe fiecare tabel
- Constraint-uri cu nume explicit — niciodată unnamed
- Stored procedures: `CREATE OR ALTER` — niciodată `CREATE` simplu
- INTERZIS: `SELECT *` în orice SP sau View
- INTERZIS: Concatenare string în SQL (SQL injection)
- INTERZIS: `NEWID()` sau `NEWSEQUENTIALID()` pe coloane PK aggregate root
