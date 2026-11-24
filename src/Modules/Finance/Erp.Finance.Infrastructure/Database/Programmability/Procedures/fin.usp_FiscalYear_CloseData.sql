CREATE OR ALTER PROCEDURE fin.usp_FiscalYear_CloseData
    @CompanyId  int,
    @Year       smallint
AS
BEGIN
    -- Datele inchiderii anului (C3 3.6): anul (cu starea lunilor), anul urmator, notele deja scrise ale inchiderii, cifrele rezervei legale
    -- (profitul brut fara impozitul pe profit si fara notele de inchidere, capitalul social 1012, rezerva legala 1061 fara nota de rezerva a
    -- anului), apoi soldurile reportate: conturile de bilant (clasele 1–5) cu sold la sfarsitul anului, pe cont, partener, angajat si moneda,
    -- fara notele inchiderii acestui an datate in anul urmator.
    SET NOCOUNT ON;

    DECLARE @YearId int, @Start date, @End date, @OpenMonths int, @NextId int, @NextStart date;
    SELECT @YearId = Id, @Start = StartDate, @End = EndDate FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND [Year] = @Year;
    IF @YearId IS NULL
        THROW 51901, N'Anul financiar nu există.', 1;
    SET @OpenMonths = (SELECT COUNT(*) FROM fin.AccountingPeriod WHERE FiscalYearId = @YearId AND Status < 3);
    SELECT @NextId = Id, @NextStart = StartDate FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND StartDate = DATEADD(DAY, 1, @End);

    DECLARE @Statutory int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);
    DECLARE @ReserveEntry bigint = (SELECT EntryId FROM fin.FiscalYearCloseEntry WHERE FiscalYearId = @YearId AND Kind = 3);

    SELECT @YearId AS YearId, @Start AS StartDate, @End AS EndDate, @OpenMonths AS OpenMonths, @NextId AS NextYearId, @NextStart AS NextStart,
        (SELECT Status FROM fin.FiscalYear WHERE Id = @YearId) AS Status,
        (SELECT -SUM(l.DebitRon - l.CreditRon) FROM fin.JournalLine AS l JOIN fin.JournalEntry AS e ON e.Id = l.EntryId JOIN fin.GlAccount AS a ON a.Id = l.AccountId
         WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.SourceKind <> 4 AND e.AccountingDate BETWEEN @Start AND @End
           AND LEFT(a.Code, 1) IN (N'6', N'7') AND a.Code NOT LIKE N'691%' AND a.Code NOT LIKE N'694%' AND a.Code NOT LIKE N'697%' AND a.Code NOT LIKE N'698%') AS GrossProfit,
        (SELECT -SUM(l.DebitRon - l.CreditRon) FROM fin.JournalLine AS l JOIN fin.JournalEntry AS e ON e.Id = l.EntryId JOIN fin.GlAccount AS a ON a.Id = l.AccountId
         WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @Start AND @End AND a.Code LIKE N'1012%') AS ShareCapital,
        (SELECT -SUM(l.DebitRon - l.CreditRon) FROM fin.JournalLine AS l JOIN fin.JournalEntry AS e ON e.Id = l.EntryId JOIN fin.GlAccount AS a ON a.Id = l.AccountId
         WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @Start AND @End AND a.Code LIKE N'1061%'
           AND (@ReserveEntry IS NULL OR e.Id <> @ReserveEntry)) AS ExistingReserve;

    SELECT Kind, EntryId FROM fin.FiscalYearCloseEntry WHERE FiscalYearId = @YearId;

    SELECT l.AccountId, a.Code AS AccountCode, l.PartnerId, l.EmployeeId, l.CurrencyCode, SUM(l.DebitRon - l.CreditRon) AS Net,
        SUM(CASE WHEN l.CurrencyCode IS NULL THEN 0 WHEN l.Side = 1 THEN l.AmountCurrency ELSE -l.AmountCurrency END) AS NetCurrency
    FROM fin.JournalLine AS l
    JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
    JOIN fin.GlAccount AS a ON a.Id = l.AccountId AND a.ChartId = @Statutory
    WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @Start AND @End AND LEFT(a.Code, 1) IN (N'1', N'2', N'3', N'4', N'5')
    GROUP BY l.AccountId, a.Code, l.PartnerId, l.EmployeeId, l.CurrencyCode
    HAVING SUM(l.DebitRon - l.CreditRon) <> 0
    ORDER BY a.Code, l.PartnerId;
END;
