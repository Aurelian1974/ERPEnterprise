CREATE OR ALTER PROCEDURE core.usp_ImportJob_Update
    @Id           int,
    @Status       tinyint,
    @ImportedRows int,
    @LastError    nvarchar(1000) = NULL,
    @ClearPayload bit,
    @UserId       int
AS
BEGIN
    -- Progresul confirmarii (loturi de 500, 4.8 pasul 4), anularea sau esecul unui lot. La final datele randurilor se sterg.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE core.ImportJob
    SET Status = @Status, ImportedRows = @ImportedRows, LastError = @LastError,
        Payload = CASE WHEN @ClearPayload = 1 THEN NULL ELSE Payload END,
        CompletedAt = CASE WHEN @Status IN (2, 3, 4) THEN SYSUTCDATETIME() ELSE CompletedAt END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id;

    IF @@ROWCOUNT = 0
        THROW 50027, N'Importul nu există.', 1;

    SELECT RowVersion FROM core.ImportJob WHERE Id = @Id;
END;
