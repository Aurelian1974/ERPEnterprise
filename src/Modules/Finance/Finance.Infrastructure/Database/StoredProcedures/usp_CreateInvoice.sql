CREATE OR ALTER PROCEDURE finance.usp_CreateInvoice
    @Id            UNIQUEIDENTIFIER,
    @TenantId      UNIQUEIDENTIFIER,
    @CustomerId    UNIQUEIDENTIFIER,
    @Currency      CHAR(3),
    @DueDate       DATE,
    @Status        NVARCHAR(20),
    @CreatedAtUtc  DATETIME2(7)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @InvoiceNumber NVARCHAR(50);

    -- Generate sequential invoice number per tenant: INV-YYYY-NNNNNN
    SELECT @InvoiceNumber = 'INV-' + CAST(YEAR(@CreatedAtUtc) AS NVARCHAR(4))
        + '-' + RIGHT('000000' + CAST(COUNT(*) + 1 AS NVARCHAR(6)), 6)
    FROM finance.invoices
    WHERE tenant_id = @TenantId
      AND YEAR(created_at_utc) = YEAR(@CreatedAtUtc);

    INSERT INTO finance.invoices
        (id, tenant_id, customer_id, invoice_number, currency, status, due_date, created_at_utc)
    VALUES
        (@Id, @TenantId, @CustomerId, @InvoiceNumber, @Currency, @Status, @DueDate, @CreatedAtUtc);
END;
