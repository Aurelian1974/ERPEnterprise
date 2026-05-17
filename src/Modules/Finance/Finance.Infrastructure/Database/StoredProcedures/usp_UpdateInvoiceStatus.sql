CREATE OR ALTER PROCEDURE finance.usp_UpdateInvoiceStatus
    @Id             UNIQUEIDENTIFIER,
    @TenantId       UNIQUEIDENTIFIER,
    @Status         NVARCHAR(20),
    @ApprovedAtUtc  DATETIME2(7) = NULL,
    @PaidAtUtc      DATETIME2(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE finance.invoices
    SET
        status          = @Status,
        approved_at_utc = COALESCE(@ApprovedAtUtc, approved_at_utc),
        paid_at_utc     = COALESCE(@PaidAtUtc,     paid_at_utc)
    WHERE id        = @Id
      AND tenant_id = @TenantId;
END;
