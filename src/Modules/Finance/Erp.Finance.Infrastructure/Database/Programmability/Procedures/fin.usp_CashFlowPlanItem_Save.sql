CREATE OR ALTER PROCEDURE fin.usp_CashFlowPlanItem_Save
    @CompanyId          int,
    @Id                 int = NULL,
    @CategoryId         int,
    @Description        nvarchar(200),
    @CurrencyCode       char(3),
    @Amount             decimal(19,2),
    @TreasuryAccountId  int = NULL,
    @FirstDate          date,
    @Recurrence         tinyint,
    @RecurrenceDay      tinyint = NULL,
    @EndDate            date = NULL,
    @IsActive           bit,
    @Probability        tinyint,
    @RowVersion         binary(8) = NULL,
    @UserId             int
AS
BEGIN
    -- O suma planificata (F5 3.5): adaugare sau modificare cu RowVersion (51702). Categoria activa si contul firmei (51021).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.CashFlowCategory WHERE Id = @CategoryId AND IsActive = 1)
       OR (@TreasuryAccountId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount WHERE Id = @TreasuryAccountId AND CompanyId = @CompanyId))
        THROW 51021, N'Categoria sau contul de trezorerie nu există.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.CashFlowPlanItem (CompanyId, CategoryId, Description, CurrencyCode, Amount, TreasuryAccountId, FirstDate, Recurrence, RecurrenceDay,
            EndDate, IsActive, Probability, CreatedBy)
        VALUES (@CompanyId, @CategoryId, @Description, @CurrencyCode, @Amount, @TreasuryAccountId, @FirstDate, @Recurrence, @RecurrenceDay, @EndDate,
            @IsActive, @Probability, @UserId);
        SELECT CAST(SCOPE_IDENTITY() AS int) AS Id;
        RETURN;
    END;

    IF NOT EXISTS (SELECT 1 FROM fin.CashFlowPlanItem WHERE Id = @Id AND CompanyId = @CompanyId)
        THROW 51712, N'Suma planificată nu există.', 1;

    UPDATE fin.CashFlowPlanItem
    SET CategoryId = @CategoryId, Description = @Description, CurrencyCode = @CurrencyCode, Amount = @Amount, TreasuryAccountId = @TreasuryAccountId,
        FirstDate = @FirstDate, Recurrence = @Recurrence, RecurrenceDay = @RecurrenceDay, EndDate = @EndDate, IsActive = @IsActive,
        Probability = @Probability, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND CompanyId = @CompanyId AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
        THROW 51702, N'Suma planificată a fost modificată de altcineva între timp.', 1;

    SELECT @Id AS Id;
END;
