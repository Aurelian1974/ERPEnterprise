CREATE OR ALTER PROCEDURE inv.usp_RetailPriceChange_Cancel
    @CompanyId  int,
    @Id         int,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Anularea ciornei MP (02.05, S3 3.7).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Version binary(8);
    SELECT @Status = Status, @Version = RowVersion FROM inv.RetailPriceChange WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 54001, N'Documentul nu există în firma curentă.', 1;
    IF @Version <> @RowVersion
        THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 1
        THROW 54118, N'Documentul nu mai este ciornă.', 1;

    UPDATE inv.RetailPriceChange SET Status = 4, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id;
    SELECT Id, RowVersion FROM inv.RetailPriceChange WHERE Id = @Id;
END;
