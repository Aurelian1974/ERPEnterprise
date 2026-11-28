CREATE OR ALTER PROCEDURE inv.usp_LandedCost_SetStatus
    @CompanyId     int,
    @Id            int,
    @Status        tinyint,
    @Number        nvarchar(40) = NULL,
    @StockAmount   decimal(19,2) = NULL,
    @ExpenseAmount decimal(19,2) = NULL,
    @RowVersion    binary(8) = NULL,
    @UserId        int
AS
BEGIN
    -- Sfarsitul validarii (Status 2: numarul si impartirea stoc / cheltuiala) sau anularea ciornei (Status 4, cu versiunea vazuta).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Status = 4
    BEGIN
        DECLARE @Current tinyint, @Version binary(8);
        SELECT @Current = Status, @Version = RowVersion FROM inv.LandedCost WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
        IF @Current IS NULL
            THROW 54001, N'Documentul nu există în firma curentă.', 1;
        IF @Version <> @RowVersion
            THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @Current <> 1
            THROW 54118, N'Documentul nu mai este ciornă.', 1;
    END;

    UPDATE inv.LandedCost
    SET Status = @Status, Number = COALESCE(@Number, Number), StockAmount = @StockAmount, ExpenseAmount = @ExpenseAmount,
        ValidatedAt = CASE WHEN @Status = 2 THEN SYSUTCDATETIME() END, ValidatedBy = CASE WHEN @Status = 2 THEN @UserId END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND CompanyId = @CompanyId;

    SELECT Id, RowVersion FROM inv.LandedCost WHERE Id = @Id;
END;
