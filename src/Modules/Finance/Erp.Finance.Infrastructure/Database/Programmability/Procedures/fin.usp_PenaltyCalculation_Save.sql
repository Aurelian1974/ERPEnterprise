CREATE OR ALTER PROCEDURE fin.usp_PenaltyCalculation_Save
    @Id               int = NULL,
    @CompanyId        int,
    @Number           nvarchar(40) = NULL,
    @CalculationDate  date,
    @PeriodFrom       date,
    @PeriodTo         date,
    @PartnerId        int = NULL,
    @DisputedCount    int,
    @Lines            fin.PenaltyLineList READONLY,
    @RowVersion       binary(8) = NULL,
    @UserId           int
AS
BEGIN
    -- Creeaza un calcul de penalitati ciorna sau ii inlocuieste liniile la recalculare (F3 4.7 pasii 1–2). Doar ciorna se recalculeaza
    -- (51423); versiune diferita: 51417. Intoarce Id-ul si versiunea.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.PenaltyCalculation (CompanyId, Number, CalculationDate, PeriodFrom, PeriodTo, PartnerId, DisputedCount, CreatedBy)
        VALUES (@CompanyId, @Number, @CalculationDate, @PeriodFrom, @PeriodTo, @PartnerId, @DisputedCount, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @Status tinyint, @Version binary(8);
        SELECT @Status = Status, @Version = RowVersion FROM fin.PenaltyCalculation WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
        IF @Status IS NULL
            THROW 51422, N'Calculul de penalități nu există.', 1;
        IF @Version <> @RowVersion
            THROW 51417, N'Calculul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @Status <> 1
            THROW 51423, N'Doar un calcul ciornă se poate modifica.', 1;
        DELETE FROM fin.PenaltyCalculationLine WHERE CalculationId = @Id;
        UPDATE fin.PenaltyCalculation SET CalculationDate = @CalculationDate, DisputedCount = @DisputedCount, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
    END;

    INSERT INTO fin.PenaltyCalculationLine (CalculationId, LineNumber, Kind, PartnerId, OpenItemId, DueDate, FromDate, ToDate, Days, BaseAmount, CurrencyCode,
        RatePerDay, AnnualRate, PenaltyAmount)
    SELECT @Id, LineNumber, Kind, PartnerId, OpenItemId, DueDate, FromDate, ToDate, Days, BaseAmount, CurrencyCode, RatePerDay, AnnualRate, PenaltyAmount
    FROM @Lines;

    SELECT Id, RowVersion FROM fin.PenaltyCalculation WHERE Id = @Id;
END;
