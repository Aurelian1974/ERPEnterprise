CREATE OR ALTER PROCEDURE fin.usp_TaxReturn_Save
    @CompanyId         int,
    @FormCode          varchar(10),
    @PeriodFrom        date,
    @PeriodTo          date,
    @DueDate           date = NULL,
    @Lines             nvarchar(max),
    @Hash              int,
    @AmountDue         decimal(19,2),
    @AmountRefundable  decimal(19,2),
    @UserId            int
AS
BEGIN
    -- Calculul unei declaratii (C4 4.1 pasul 1): ciorna initiala a perioadei, creata sau recalculata. Randurile calculate se rescriu (decizia 4),
    -- valorile manuale ale randurilor manuale raman; amprenta datelor si sumele se actualizeaza; declaratia depusa nu se mai recalculeaza (51947),
    -- iar recalcularea readuce o declaratie verificata sau generata in Ciorna. Intoarce Id-ul.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    DECLARE @Id int, @Status tinyint;
    SELECT @Id = Id, @Status = Status FROM fin.TaxReturn WITH (UPDLOCK, HOLDLOCK)
    WHERE CompanyId = @CompanyId AND FormCode = @FormCode AND PeriodFrom = @PeriodFrom AND Kind = 1 AND Status <> 5;
    IF @Status = 4
        THROW 51947, N'Declarația e depusă; o corecție se face prin rectificativă.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.TaxReturn (CompanyId, FormCode, PeriodFrom, PeriodTo, DueDate, CalculatedAt, CalculatedBy, CalculationHash, AmountDue, AmountRefundable, CreatedBy)
        VALUES (@CompanyId, @FormCode, @PeriodFrom, @PeriodTo, @DueDate, SYSUTCDATETIME(), @UserId, @Hash, @AmountDue, @AmountRefundable, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE fin.TaxReturn
        SET Status = 1, PeriodTo = @PeriodTo, DueDate = ISNULL(@DueDate, DueDate), CalculatedAt = SYSUTCDATETIME(), CalculatedBy = @UserId, CalculationHash = @Hash,
            AmountDue = @AmountDue, AmountRefundable = @AmountRefundable, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;

    DECLARE @New TABLE (RowCode varchar(30), ColumnCode varchar(20), Label nvarchar(200), SortOrder int, IsManual bit, CalculatedValue decimal(19,2));
    INSERT INTO @New
    SELECT RowCode, ColumnCode, Label, SortOrder, ISNULL(IsManual, 0), CalculatedValue
    FROM OPENJSON(@Lines) WITH (RowCode varchar(30) '$.rowCode', ColumnCode varchar(20) '$.columnCode', Label nvarchar(200) '$.label', SortOrder int '$.sortOrder',
        IsManual bit '$.isManual', CalculatedValue decimal(19,2) '$.calculatedValue');

    DELETE l FROM fin.TaxReturnLine AS l
    WHERE l.ReturnId = @Id AND NOT EXISTS (SELECT 1 FROM @New AS n WHERE n.RowCode = l.RowCode AND n.ColumnCode = l.ColumnCode);
    UPDATE l SET Label = n.Label, SortOrder = n.SortOrder, IsManual = n.IsManual, CalculatedValue = n.CalculatedValue,
        ManualValue = CASE WHEN n.IsManual = 1 THEN l.ManualValue END, ManualReason = CASE WHEN n.IsManual = 1 THEN l.ManualReason END
    FROM fin.TaxReturnLine AS l JOIN @New AS n ON n.RowCode = l.RowCode AND n.ColumnCode = l.ColumnCode
    WHERE l.ReturnId = @Id;
    INSERT INTO fin.TaxReturnLine (ReturnId, RowCode, ColumnCode, Label, SortOrder, IsManual, CalculatedValue)
    SELECT @Id, n.RowCode, n.ColumnCode, n.Label, n.SortOrder, n.IsManual, n.CalculatedValue
    FROM @New AS n WHERE NOT EXISTS (SELECT 1 FROM fin.TaxReturnLine AS l WHERE l.ReturnId = @Id AND l.RowCode = n.RowCode AND l.ColumnCode = n.ColumnCode);
    COMMIT TRANSACTION;

    SELECT @Id;
END;
