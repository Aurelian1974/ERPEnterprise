CREATE OR ALTER PROCEDURE fin.usp_PaymentBatch_Cancel
    @CompanyId  int,
    @Id         int,
    @Reason     nvarchar(500),
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Anularea unui lot (02.02, F2 4.7: exportat dar neincarcat la banca, sau deschis): OP-urile raman validate si pot intra intr-un lot
    -- nou. Un lot cu un OP deja confirmat pe extras nu se anuleaza (51217).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @BatchCompanyId int, @Version binary(8);
    SELECT @Status = Status, @BatchCompanyId = CompanyId, @Version = RowVersion FROM fin.PaymentBatch WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
    IF @Status IS NULL OR @BatchCompanyId <> @CompanyId
        THROW 51226, N'Lotul de plăți nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51202, N'Lotul de plăți a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status = 3
        THROW 51222, N'Lotul este deja anulat.', 1;
    IF EXISTS (SELECT 1 FROM fin.PaymentOrder WHERE PaymentBatchId = @Id AND ConfirmationStatus = 2)
        THROW 51217, N'Lotul are ordine de plată deja confirmate pe extras.', 1;

    UPDATE fin.PaymentOrder SET PaymentBatchId = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE PaymentBatchId = @Id;
    UPDATE fin.PaymentBatch
    SET Status = 3, CancelledAt = SYSUTCDATETIME(), CancelledBy = @UserId, CancelReason = @Reason, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id;

    SELECT RowVersion FROM fin.PaymentBatch WHERE Id = @Id;
END;
