CREATE OR ALTER PROCEDURE inv.usp_ValuationMethodChange_Save
    @Id            int = NULL,
    @CompanyId     int,
    @NewMethod     tinyint,
    @EffectiveDate date,
    @Reason        nvarchar(1000),
    @ItemIds       inv.IdList READONLY,
    @RowVersion    binary(8) = NULL,
    @UserId        int
AS
BEGIN
    -- Ciorna documentului SM (02.05, S1 4.5): modelele stocabile, metoda noua, prima zi a lunii (54011), motivul; identificarea directa cere
    -- urmarirea pe lot sau serie (54015). Liniile se inlocuiesc.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF DAY(@EffectiveDate) <> 1
        THROW 54011, N'Schimbarea metodei se aplică de la prima zi a unei luni.', 1;
    IF NOT EXISTS (SELECT 1 FROM @ItemIds)
        THROW 54018, N'Documentul trebuie să aibă cel puțin un articol.', 1;
    IF EXISTS (SELECT 1 FROM @ItemIds AS x LEFT JOIN core.Item AS i ON i.Id = x.Id WHERE i.Id IS NULL OR i.IsStockable = 0)
        THROW 54018, N'Un articol nu există sau nu este stocabil.', 1;
    IF @NewMethod = 4 AND EXISTS (SELECT 1 FROM @ItemIds AS x LEFT JOIN inv.ItemStockSettings AS s ON s.CompanyId = @CompanyId AND s.ItemId = x.Id
                                  WHERE ISNULL(s.TrackingMode, 0) = 0)
        THROW 54015, N'Identificarea directă cere urmărirea pe lot sau pe serie.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO inv.ValuationMethodChange (CompanyId, NewMethod, EffectiveDate, Reason, CreatedBy)
        VALUES (@CompanyId, @NewMethod, @EffectiveDate, @Reason, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @Status tinyint, @Version binary(8);
        SELECT @Status = Status, @Version = RowVersion FROM inv.ValuationMethodChange WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
        IF @Status IS NULL
            THROW 54001, N'Documentul nu există în firma curentă.', 1;
        IF @RowVersion IS NULL OR @Version <> @RowVersion
            THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @Status <> 1
            THROW 54118, N'Documentul nu mai este ciornă.', 1;
        UPDATE inv.ValuationMethodChange
        SET NewMethod = @NewMethod, EffectiveDate = @EffectiveDate, Reason = @Reason, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
        DELETE FROM inv.ValuationMethodChangeLine WHERE ChangeId = @Id;
    END;

    INSERT INTO inv.ValuationMethodChangeLine (ChangeId, ItemId) SELECT @Id, Id FROM @ItemIds;
    SELECT Id, RowVersion FROM inv.ValuationMethodChange WHERE Id = @Id;
END;
