CREATE OR ALTER PROCEDURE fin.usp_FiscalYear_Create
    @CompanyId       int,
    @Year            smallint,
    @StartDate       date,
    @EndDate         date,
    @VatPeriodicity  tinyint,
    @UserId          int
AS
BEGIN
    -- Un an financiar cu lunile lui (C3 3.1, 3.2): luna 1 incepe la StartDate, ultima se termina la EndDate (primul exercitiu poate fi mai
    -- scurt). Anul nu se suprapune peste altul al firmei (51911); un an existent intoarce Id-ul lui (idempotent pentru job).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Id int = (SELECT Id FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND [Year] = @Year);
    IF @Id IS NOT NULL
    BEGIN
        SELECT @Id AS Id;
        RETURN;
    END;

    IF EXISTS (SELECT 1 FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND StartDate <= @EndDate AND EndDate >= @StartDate)
        THROW 51911, N'Anul se suprapune peste un an financiar existent al firmei.', 1;

    BEGIN TRANSACTION;
    INSERT INTO fin.FiscalYear (CompanyId, [Year], StartDate, EndDate, VatPeriodicity, CreatedBy)
    VALUES (@CompanyId, @Year, @StartDate, @EndDate, @VatPeriodicity, @UserId);
    SET @Id = CAST(SCOPE_IDENTITY() AS int);

    WITH Months AS (
        SELECT 1 AS PeriodNo, @StartDate AS StartDate
        UNION ALL
        SELECT PeriodNo + 1, DATEADD(DAY, 1, EOMONTH(StartDate)) FROM Months WHERE EOMONTH(StartDate) < @EndDate AND PeriodNo < 12)
    INSERT INTO fin.AccountingPeriod (CompanyId, FiscalYearId, PeriodNo, StartDate, EndDate, CreatedBy)
    SELECT @CompanyId, @Id, PeriodNo, StartDate, CASE WHEN EOMONTH(StartDate) > @EndDate THEN @EndDate ELSE EOMONTH(StartDate) END, @UserId FROM Months;
    COMMIT TRANSACTION;

    SELECT @Id AS Id;
END;
