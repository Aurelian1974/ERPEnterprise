CREATE OR ALTER PROCEDURE fin.usp_ProfitTax_Data
    @CompanyId  int,
    @Year       smallint,
    @Quarter    tinyint
AS
BEGIN
    -- Datele fisei de calcul (C4 3.6): rulajele cumulate de la inceputul anului financiar pana la sfarsitul trimestrului ale conturilor din
    -- clasele 6 si 7 pe tratamentul fiscal (1 deductibil, 2 nedeductibil, 3 limitat, 4 neimpozabil), fara 691 si 698 si fara notele de
    -- inchidere a claselor 6 si 7; apoi perioada, starea ultimei luni, regimul si cota din setari, impozitul trimestrelor anterioare si
    -- randurile manuale ale fisei existente.
    SET NOCOUNT ON;
    DECLARE @YearStart date, @YearId int;
    SELECT @YearId = Id, @YearStart = StartDate FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND Year = @Year;
    IF @YearId IS NULL
        THROW 51901, N'Anul financiar nu există.', 1;
    DECLARE @QuarterEnd date = EOMONTH(DATEADD(MONTH, 3 * @Quarter - 1, @YearStart));
    DECLARE @Statutory int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);

    SELECT LEFT(a.Code, 1) AS Class, a.TaxTreatment, SUM(l.DebitRon - l.CreditRon) AS Net
    FROM fin.JournalLine AS l
    JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
    JOIN fin.GlAccount AS a ON a.Id = l.AccountId AND a.ChartId = @Statutory
    WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @YearStart AND @QuarterEnd
      AND LEFT(a.Code, 1) IN (N'6', N'7') AND a.Code NOT LIKE N'691%' AND a.Code NOT LIKE N'698%'
      AND NOT EXISTS (SELECT 1 FROM fin.PeriodClosingEntry AS pc WHERE pc.EntryId = e.Id AND pc.Kind = 2)
    GROUP BY LEFT(a.Code, 1), a.TaxTreatment;

    SELECT @YearStart AS YearStart, @QuarterEnd AS QuarterEnd,
        (SELECT Status FROM fin.AccountingPeriod WHERE CompanyId = @CompanyId AND EndDate = @QuarterEnd) AS LastMonthStatus,
        ISNULL(TRY_CAST((SELECT Value FROM fin.CompanySetting WHERE CompanyId = @CompanyId AND Code = N'Fin.Tax.ProfitRegime') AS tinyint), 1) AS Regime,
        TRY_CAST((SELECT Value FROM fin.CompanySetting WHERE CompanyId = @CompanyId AND Code = N'Fin.Tax.Rate') AS decimal(5,2)) AS Rate,
        ISNULL((SELECT SUM(ISNULL(PostedAmount, TaxDue)) FROM fin.ProfitTaxComputation WHERE CompanyId = @CompanyId AND Year = @Year AND Quarter < @Quarter), 0)
            AS PriorTax;

    SELECT l.RowCode, l.Amount, l.Reason FROM fin.ProfitTaxComputationLine AS l
    JOIN fin.ProfitTaxComputation AS c ON c.Id = l.ComputationId
    WHERE c.CompanyId = @CompanyId AND c.Year = @Year AND c.Quarter = @Quarter AND l.IsManual = 1;

    SELECT Id, Code FROM fin.GlAccount WHERE ChartId = @Statutory AND Code IN (N'691', N'4411');
END;
