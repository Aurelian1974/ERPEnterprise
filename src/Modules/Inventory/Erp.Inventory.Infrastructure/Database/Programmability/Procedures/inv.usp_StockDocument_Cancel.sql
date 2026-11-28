CREATE OR ALTER PROCEDURE inv.usp_StockDocument_Cancel
    @CompanyId  int,
    @Id         int,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Anularea unei ciorne (02.05, S2 3.3): ramane in lista, fara numar si fara miscari. Coduri: 54001, 54117, 54118.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint = (SELECT Status FROM inv.StockDocument WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId);
    IF @Status IS NULL
        THROW 54001, N'Documentul nu există în firma curentă.', 1;
    IF @Status <> 1
        THROW 54118, N'Se anulează doar o ciornă.', 1;

    UPDATE inv.StockDocument SET Status = 4, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
        THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;

    SELECT Id, RowVersion FROM inv.StockDocument WHERE Id = @Id;
END;
