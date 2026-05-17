---
name: sql-objects
description: >-
  Generează obiecte SQL corecte pentru ERP: Stored Procedures, Views,
  Table-Valued Functions, Scalar Functions, Triggers. Toate obiectele
  respectă convențiile stricte: schema prefix, tenant_id, CREATE OR ALTER,
  parametri tipizați, fără SELECT *, fără SQL dinamic necontrolat.
---

# SQL Objects

## Când se aplică
Când utilizatorul cere să scrie sau să modifice un Stored Procedure, View,
Table-Valued Function, Scalar Function sau Trigger pentru ERP.

## Reguli absolute

```
CREATE OR ALTER     — ÎNTOTDEAUNA, niciodată CREATE simplu
Schema prefix       — ÎNTOTDEAUNA (finance., hr., inventory.)
tenant_id           — în ORICE obiect care accesează date
SELECT *            — INTERZIS, coloane explicite întotdeauna
SQL dinamic         — INTERZIS fără parametrizare (sp_executesql cu @params)
Concatenare string  — INTERZIS în SQL (SQL injection)
SET NOCOUNT ON      — pe fiecare SP și Trigger
Alias explicit      — AS NumeAlias pe orice coloană returnată către C#
SYSUTCDATETIME()    — pentru datetime, niciodată GETDATE()
```

## Naming

| Obiect | Convenție | Exemplu |
|---|---|---|
| Stored Procedure | `usp_{Action}{Entity}` | `finance.usp_CreateInvoice` |
| View | `vw_{Description}` | `finance.vw_InvoiceAging` |
| Table-Valued Function | `tvf_{Description}` | `finance.tvf_InvoiceLines` |
| Scalar Function | `fn_{Description}` | `finance.fn_CalculateVAT` |
| Trigger | `trg_{Table}_{Timing}_{Action}` | `finance.trg_invoices_After_Update` |

---

## Template SP — INSERT

```sql
CREATE OR ALTER PROCEDURE finance.usp_CreateInvoice
    @Id             UNIQUEIDENTIFIER,   -- vine din C# (UUIDv7), nu generat în SQL
    @TenantId       UNIQUEIDENTIFIER,
    @CustomerId     UNIQUEIDENTIFIER,
    @InvoiceNumber  NVARCHAR(50),
    @DueDate        DATE,
    @TotalAmount    DECIMAL(18,4)       = 0,
    @CreatedBy      UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM finance.invoices i
        WHERE i.tenant_id      = @TenantId
          AND i.invoice_number = @InvoiceNumber
    )
    BEGIN
        RAISERROR('Invoice number already exists for this tenant.', 16, 1);
        RETURN;
    END;

    INSERT INTO finance.invoices (
        id, tenant_id, customer_id, invoice_number,
        due_date, total_amount, status, created_at, created_by
    )
    VALUES (
        @Id, @TenantId, @CustomerId, @InvoiceNumber,
        @DueDate, @TotalAmount, 1, SYSUTCDATETIME(), @CreatedBy
    );
END;
GO
```

## Template SP — SELECT single row

```sql
CREATE OR ALTER PROCEDURE finance.usp_GetInvoiceById
    @Id       UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT i.id                AS Id,
           i.tenant_id         AS TenantId,
           i.customer_id       AS CustomerId,
           c.name              AS CustomerName,
           i.invoice_number    AS InvoiceNumber,
           i.status            AS Status,
           i.due_date          AS DueDate,
           i.total_amount      AS TotalAmount,
           i.created_at        AS CreatedAt,
           i.created_by        AS CreatedBy,
           i.updated_at        AS UpdatedAt
    FROM finance.invoices i
    INNER JOIN finance.customers c
        ON c.id        = i.customer_id
       AND c.tenant_id = i.tenant_id     -- tenant_id pe fiecare JOIN
    WHERE i.id        = @Id
      AND i.tenant_id = @TenantId;
END;
GO
```

## Template SP — SELECT paginat cu filtre

```sql
CREATE OR ALTER PROCEDURE finance.usp_ListInvoices
    @TenantId    UNIQUEIDENTIFIER,
    @Search      NVARCHAR(200)    = NULL,
    @Status      TINYINT          = NULL,
    @CustomerId  UNIQUEIDENTIFIER = NULL,
    @DateFrom    DATE             = NULL,
    @DateTo      DATE             = NULL,
    @Page        INT              = 1,
    @PageSize    INT              = 25,
    @SortColumn  NVARCHAR(50)     = 'created_at',
    @SortDir     NVARCHAR(4)      = 'DESC'
AS
BEGIN
    SET NOCOUNT ON;

    IF @Page < 1 SET @Page = 1;
    IF @PageSize < 1 OR @PageSize > 500 SET @PageSize = 25;

    SELECT COUNT(*) OVER ()    AS TotalCount,
           i.id                AS Id,
           i.invoice_number    AS InvoiceNumber,
           i.status            AS Status,
           i.due_date          AS DueDate,
           i.total_amount      AS TotalAmount,
           c.name              AS CustomerName,
           i.created_at        AS CreatedAt
    FROM finance.invoices i
    INNER JOIN finance.customers c
        ON c.id        = i.customer_id
       AND c.tenant_id = i.tenant_id
    WHERE i.tenant_id  = @TenantId
      AND (@Status      IS NULL OR i.status      = @Status)
      AND (@CustomerId  IS NULL OR i.customer_id = @CustomerId)
      AND (@DateFrom    IS NULL OR i.due_date   >= @DateFrom)
      AND (@DateTo      IS NULL OR i.due_date   <= @DateTo)
      AND (@Search      IS NULL
           OR i.invoice_number LIKE '%' + @Search + '%'
           OR c.name           LIKE '%' + @Search + '%')
    ORDER BY
        CASE WHEN @SortColumn = 'invoice_number' AND @SortDir = 'ASC'  THEN i.invoice_number END ASC,
        CASE WHEN @SortColumn = 'invoice_number' AND @SortDir = 'DESC' THEN i.invoice_number END DESC,
        CASE WHEN @SortColumn = 'due_date'       AND @SortDir = 'ASC'  THEN i.due_date      END ASC,
        CASE WHEN @SortColumn = 'due_date'       AND @SortDir = 'DESC' THEN i.due_date      END DESC,
        CASE WHEN @SortColumn = 'total_amount'   AND @SortDir = 'ASC'  THEN i.total_amount  END ASC,
        CASE WHEN @SortColumn = 'total_amount'   AND @SortDir = 'DESC' THEN i.total_amount  END DESC,
        i.created_at DESC   -- fallback sort
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END;
GO
```

## Template SP — UPDATE

```sql
CREATE OR ALTER PROCEDURE finance.usp_UpdateInvoice
    @Id          UNIQUEIDENTIFIER,
    @TenantId    UNIQUEIDENTIFIER,
    @DueDate     DATE,
    @TotalAmount DECIMAL(18,4),
    @UpdatedBy   UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE finance.invoices
    SET due_date     = @DueDate,
        total_amount = @TotalAmount,
        updated_at   = SYSUTCDATETIME(),
        updated_by   = @UpdatedBy
    WHERE id        = @Id
      AND tenant_id = @TenantId
      AND status    = 1;    -- numai Draft

    SELECT @@ROWCOUNT AS AffectedRows;
END;
GO
```

## Template SP — DELETE (soft delete recomandat în ERP)

```sql
CREATE OR ALTER PROCEDURE finance.usp_DeleteInvoice
    @Id        UNIQUEIDENTIFIER,
    @TenantId  UNIQUEIDENTIFIER,
    @DeletedBy UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE finance.invoices
    SET is_deleted = 1,
        deleted_at = SYSUTCDATETIME(),
        deleted_by = @DeletedBy
    WHERE id        = @Id
      AND tenant_id = @TenantId
      AND status    = 1;    -- numai Draft

    SELECT @@ROWCOUNT AS AffectedRows;
END;
GO
```

## Template SP — raport cu CTE

```sql
CREATE OR ALTER PROCEDURE finance.usp_GetInvoiceAgingReport
    @TenantId UNIQUEIDENTIFIER,
    @AsOfDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @AsOfDate IS NULL SET @AsOfDate = CAST(SYSUTCDATETIME() AS DATE);

    WITH aging_base AS (
        SELECT i.id                                          AS InvoiceId,
               i.invoice_number                              AS InvoiceNumber,
               c.name                                        AS CustomerName,
               i.total_amount                                AS TotalAmount,
               i.due_date                                    AS DueDate,
               DATEDIFF(DAY, i.due_date, @AsOfDate)          AS DaysOverdue
        FROM finance.invoices i
        INNER JOIN finance.customers c
            ON c.id        = i.customer_id
           AND c.tenant_id = i.tenant_id
        WHERE i.tenant_id = @TenantId
          AND i.status    = 2               -- Approved
          AND i.due_date  < @AsOfDate
    )
    SELECT ab.InvoiceId,
           ab.InvoiceNumber,
           ab.CustomerName,
           ab.TotalAmount,
           ab.DueDate,
           ab.DaysOverdue,
           CASE
               WHEN ab.DaysOverdue BETWEEN 1  AND 30  THEN '1-30'
               WHEN ab.DaysOverdue BETWEEN 31 AND 60  THEN '31-60'
               WHEN ab.DaysOverdue BETWEEN 61 AND 90  THEN '61-90'
               ELSE '90+'
           END AS AgingBucket
    FROM aging_base ab
    ORDER BY ab.DaysOverdue DESC,
             ab.CustomerName ASC;
END;
GO
```

## Template View

```sql
-- View — join-uri sau agregări fixe reutilizabile
-- Nu filtrează tenant_id — SP-ul care o citește adaugă filtrul
CREATE OR ALTER VIEW finance.vw_InvoiceAging
AS
    SELECT i.tenant_id                                           AS TenantId,
           i.id                                                  AS InvoiceId,
           i.invoice_number                                      AS InvoiceNumber,
           c.name                                                AS CustomerName,
           i.total_amount                                        AS TotalAmount,
           i.due_date                                            AS DueDate,
           DATEDIFF(DAY, i.due_date, CAST(SYSUTCDATETIME() AS DATE)) AS DaysOverdue,
           CASE
               WHEN DATEDIFF(DAY, i.due_date, CAST(SYSUTCDATETIME() AS DATE)) BETWEEN 1  AND 30  THEN '1-30'
               WHEN DATEDIFF(DAY, i.due_date, CAST(SYSUTCDATETIME() AS DATE)) BETWEEN 31 AND 60  THEN '31-60'
               WHEN DATEDIFF(DAY, i.due_date, CAST(SYSUTCDATETIME() AS DATE)) BETWEEN 61 AND 90  THEN '61-90'
               ELSE '90+'
           END                                                   AS AgingBucket
    FROM finance.invoices i
    INNER JOIN finance.customers c
        ON c.id        = i.customer_id
       AND c.tenant_id = i.tenant_id
    WHERE i.status   = 2
      AND i.due_date < CAST(SYSUTCDATETIME() AS DATE);
GO

-- SP care citește din view și aplică tenant_id
CREATE OR ALTER PROCEDURE finance.usp_GetInvoiceAging
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT v.InvoiceId,
           v.InvoiceNumber,
           v.CustomerName,
           v.TotalAmount,
           v.DueDate,
           v.DaysOverdue,
           v.AgingBucket
    FROM finance.vw_InvoiceAging v
    WHERE v.TenantId = @TenantId
    ORDER BY v.DaysOverdue DESC;
END;
GO
```

## Template Table-Valued Function (TVF)

```sql
-- TVF inline — returnează tabel, poate fi folosit în JOIN din alt SP
CREATE OR ALTER FUNCTION finance.tvf_InvoiceLines
(
    @InvoiceId UNIQUEIDENTIFIER,
    @TenantId  UNIQUEIDENTIFIER
)
RETURNS TABLE
AS
RETURN
(
    SELECT il.id            AS Id,
           il.product_id    AS ProductId,
           p.name           AS ProductName,
           il.description   AS Description,
           il.quantity      AS Quantity,
           il.unit_price    AS UnitPrice,
           il.vat_rate      AS VatRate,
           il.line_total    AS LineTotal,
           il.sort_order    AS SortOrder
    FROM finance.invoice_lines il
    INNER JOIN inventory.products p
        ON p.id        = il.product_id
       AND p.tenant_id = il.tenant_id
    WHERE il.invoice_id = @InvoiceId
      AND il.tenant_id  = @TenantId
);
GO

-- Utilizare TVF în SP
CREATE OR ALTER PROCEDURE finance.usp_GetInvoiceLines
    @InvoiceId UNIQUEIDENTIFIER,
    @TenantId  UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT l.Id,
           l.ProductId,
           l.ProductName,
           l.Description,
           l.Quantity,
           l.UnitPrice,
           l.VatRate,
           l.LineTotal,
           l.SortOrder
    FROM finance.tvf_InvoiceLines(@InvoiceId, @TenantId) l
    ORDER BY l.SortOrder;
END;
GO
```

## Template Scalar Function

```sql
-- Scalar Function — calcul pur, fără acces la tabele
CREATE OR ALTER FUNCTION finance.fn_CalculateVAT
(
    @Amount  DECIMAL(18,4),
    @VatRate DECIMAL(5,4)
)
RETURNS DECIMAL(18,4)
AS
BEGIN
    RETURN ROUND(@Amount * @VatRate, 4);
END;
GO
```

## Template Trigger (folosit excepțional)

```sql
-- Trigger — preferă audit din MediatR behavior față de trigger
-- Folosit doar unde aplicația nu poate controla fluxul
CREATE OR ALTER TRIGGER finance.trg_invoices_After_Update
ON finance.invoices
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO audit.audit_log (
        tenant_id, user_id, user_name,
        action, entity_type, entity_id,
        old_values, new_values, created_at
    )
    SELECT d.tenant_id,
           '00000000-0000-0000-0000-000000000000',
           'SYSTEM',
           'Invoice.Update',
           'Invoice',
           CAST(d.id AS NVARCHAR(100)),
           (SELECT d.status, d.total_amount FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           (SELECT i.status, i.total_amount FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           SYSUTCDATETIME()
    FROM deleted d
    INNER JOIN inserted i ON i.id = d.id;
END;
GO
```

## Checklist înainte de a finaliza un obiect SQL

- [ ] `CREATE OR ALTER` — nu `CREATE` simplu
- [ ] `SET NOCOUNT ON` — pe SP și Trigger
- [ ] Schema prefix explicit pe toate tabelele și obiectele
- [ ] `tenant_id = @TenantId` în WHERE pe fiecare tabel accesat
- [ ] `AND t.tenant_id = @TenantId` pe fiecare JOIN
- [ ] Zero `SELECT *` — coloane explicit cu alias AS
- [ ] `SYSUTCDATETIME()` pentru datetime — nu `GETDATE()`
- [ ] Parametri cu valori default unde are sens (`= NULL`, `= 1`, `= 25`)
- [ ] `@@ROWCOUNT` returnat din UPDATE/DELETE ca `AffectedRows`
- [ ] Niciodată concatenare string în SQL
