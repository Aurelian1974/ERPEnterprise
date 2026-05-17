CREATE OR ALTER PROCEDURE finance.usp_GetInvoiceById
    @Id       UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        i.id              AS Id,
        i.tenant_id       AS TenantId,
        i.customer_id     AS CustomerId,
        i.invoice_number  AS InvoiceNumber,
        i.currency        AS Currency,
        i.status          AS Status,
        i.due_date        AS DueDate,
        i.created_at_utc  AS CreatedAtUtc,
        i.approved_at_utc AS ApprovedAtUtc,
        i.paid_at_utc     AS PaidAtUtc,
        SUM(il.quantity * il.unit_price)                           AS TotalNet,
        SUM(il.quantity * il.unit_price * il.vat_rate / 100.0)     AS TotalVat,
        SUM(il.quantity * il.unit_price * (1 + il.vat_rate / 100.0)) AS TotalGross
    FROM finance.invoices i
    INNER JOIN finance.invoice_lines il ON il.invoice_id = i.id
    WHERE i.id = @Id
      AND i.tenant_id = @TenantId
    GROUP BY
        i.id,
        i.tenant_id,
        i.customer_id,
        i.invoice_number,
        i.currency,
        i.status,
        i.due_date,
        i.created_at_utc,
        i.approved_at_utc,
        i.paid_at_utc;
END;
