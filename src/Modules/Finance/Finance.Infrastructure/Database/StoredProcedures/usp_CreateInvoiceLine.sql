CREATE OR ALTER PROCEDURE finance.usp_CreateInvoiceLine
    @Id          UNIQUEIDENTIFIER,
    @InvoiceId   UNIQUEIDENTIFIER,
    @Description NVARCHAR(500),
    @Quantity    DECIMAL(18,6),
    @UnitPrice   DECIMAL(18,4),
    @VatRate     DECIMAL(5,2)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO finance.invoice_lines
        (invoice_id, description, quantity, unit_price, vat_rate)
    VALUES
        (@InvoiceId, @Description, @Quantity, @UnitPrice, @VatRate);
END;
