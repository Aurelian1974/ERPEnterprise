CREATE OR ALTER PROCEDURE fin.usp_TaxReturn_SaveComparison
    @CompanyId  int,
    @Id         int,
    @Rows       nvarchar(max),
    @Imported   bit,
    @UserId     int
AS
BEGIN
    -- Comparatia cu P300 (C4 3.4): randurile calculate de aplicatie inlocuiesc comparatia; explicatiile randurilor ramase raman. @Imported = 1 la
    -- preluarea unui P300 nou (data preluarii), 0 la recalcularea declaratiei.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Status tinyint = (SELECT Status FROM fin.TaxReturn WHERE Id = @Id AND CompanyId = @CompanyId);
    IF @Status IS NULL
        THROW 51941, N'Declarația nu există.', 1;
    IF @Status >= 4
        THROW 51947, N'Declarația e depusă; o corecție se face prin rectificativă.', 1;

    DECLARE @New TABLE (RowCode varchar(30), ColumnCode varchar(20), DeclaredValue decimal(19,2), PrecompletedValue decimal(19,2), IsOverThreshold bit);
    INSERT INTO @New
    SELECT RowCode, ColumnCode, DeclaredValue, PrecompletedValue, IsOverThreshold
    FROM OPENJSON(@Rows) WITH (RowCode varchar(30) '$.rowCode', ColumnCode varchar(20) '$.columnCode', DeclaredValue decimal(19,2) '$.declared',
        PrecompletedValue decimal(19,2) '$.precompleted', IsOverThreshold bit '$.isOverThreshold');

    BEGIN TRANSACTION;
    DELETE c FROM fin.TaxReturnComparison AS c
    WHERE c.ReturnId = @Id AND NOT EXISTS (SELECT 1 FROM @New AS n WHERE n.RowCode = c.RowCode AND n.ColumnCode = c.ColumnCode);
    UPDATE c SET DeclaredValue = n.DeclaredValue, PrecompletedValue = n.PrecompletedValue, IsOverThreshold = n.IsOverThreshold
    FROM fin.TaxReturnComparison AS c JOIN @New AS n ON n.RowCode = c.RowCode AND n.ColumnCode = c.ColumnCode WHERE c.ReturnId = @Id;
    INSERT INTO fin.TaxReturnComparison (ReturnId, RowCode, ColumnCode, DeclaredValue, PrecompletedValue, IsOverThreshold)
    SELECT @Id, n.RowCode, n.ColumnCode, n.DeclaredValue, n.PrecompletedValue, n.IsOverThreshold FROM @New AS n
    WHERE NOT EXISTS (SELECT 1 FROM fin.TaxReturnComparison AS c WHERE c.ReturnId = @Id AND c.RowCode = n.RowCode AND c.ColumnCode = n.ColumnCode);
    IF @Imported = 1
        UPDATE fin.TaxReturn SET PrecompletedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id;
    COMMIT;
END;
