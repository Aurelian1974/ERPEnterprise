CREATE OR ALTER PROCEDURE fin.usp_BalanceConfirmationBatch_MarkSent
    @CompanyId int,
    @BatchId   int
AS
BEGIN
    -- Data ultimei trimiteri a unui lot de confirmari de sold (F3 4.6 pasul 2).
    SET NOCOUNT ON;

    UPDATE fin.BalanceConfirmationBatch SET SentAt = SYSUTCDATETIME() WHERE Id = @BatchId AND CompanyId = @CompanyId;
END;
