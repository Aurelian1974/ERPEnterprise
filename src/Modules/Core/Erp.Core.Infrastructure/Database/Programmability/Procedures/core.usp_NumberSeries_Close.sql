CREATE OR ALTER PROCEDURE core.usp_NumberSeries_Close
    @Id         int,
    @CompanyId  int,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Inchiderea unei serii (02.01, 11.1, macheta 07c): nu se mai aloca numere din ea; inchiderea se trece in audit.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE core.NumberSeries
    SET ClosedAt = SYSUTCDATETIME(), IsDefault = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND CompanyId = @CompanyId AND RowVersion = @RowVersion AND ClosedAt IS NULL;

    IF @@ROWCOUNT = 0
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM core.NumberSeries WHERE Id = @Id AND CompanyId = @CompanyId)
            THROW 50027, N'Seria nu există în această firmă.', 1;
        IF EXISTS (SELECT 1 FROM core.NumberSeries WHERE Id = @Id AND ClosedAt IS NOT NULL)
            THROW 50025, N'Seria este deja închisă.', 1;
        THROW 50006, N'Seria a fost modificată între timp (de alt utilizator sau de o validare); reîncărcați datele.', 1;
    END;

    SELECT Id, RowVersion FROM core.NumberSeries WHERE Id = @Id;
END;
