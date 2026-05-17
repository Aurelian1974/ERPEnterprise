CREATE OR ALTER PROCEDURE finance.usp_ListInvoicesPaged
    @TenantId   UNIQUEIDENTIFIER,
    @Status     NVARCHAR(20)     = NULL,
    @CustomerId UNIQUEIDENTIFIER = NULL,
    @DueDateFrom DATE            = NULL,
    @DueDateTo   DATE            = NULL,
    @Page        INT             = 1,
    @PageSize    INT             = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        i.id             AS Id,
        i.invoice_number AS InvoiceNumber,
        i.customer_id    AS CustomerId,
        i.currency       AS Currency,
        i.status         AS Status,
        i.due_date       AS DueDate,
        SUM(il.quantity * il.unit_price * (1 + il.vat_rate / 100.0)) AS TotalGross,
        i.created_at_utc AS CreatedAtUtc
    FROM finance.invoices i
    INNER JOIN finance.invoice_lines il ON il.invoice_id = i.id
    WHERE i.tenant_id = @TenantId
      AND (@Status     IS NULL OR i.status      = @Status)
      AND (@CustomerId IS NULL OR i.customer_id = @CustomerId)
      AND (@DueDateFrom IS NULL OR i.due_date  >= @DueDateFrom)
      AND (@DueDateTo   IS NULL OR i.due_date  <= @DueDateTo)
    GROUP BY
        i.id,
        i.invoice_number,
        i.customer_id,
        i.currency,
        i.status,
        i.due_date,
        i.created_at_utc
    ORDER BY i.created_at_utc DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END;
