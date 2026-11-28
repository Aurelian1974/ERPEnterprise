CREATE OR ALTER PROCEDURE fin.usp_ProfitTax_Get
    @CompanyId  int,
    @Year       smallint = NULL,
    @Quarter    tinyint = NULL,
    @PeriodTo   date = NULL
AS
BEGIN
    -- Fisa de calcul a unui trimestru (macheta c4-05): antetul, cu nota contata si amprenta randurilor, apoi randurile.
    -- Cu @PeriodTo (D100, D101): trimestrul anului financiar care se termina la acea data.
    SET NOCOUNT ON;
    IF @PeriodTo IS NOT NULL
        SELECT @Year = Year, @Quarter = DATEDIFF(MONTH, StartDate, @PeriodTo) / 3 + 1
        FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND @PeriodTo BETWEEN StartDate AND EndDate;
    DECLARE @Id int = (SELECT Id FROM fin.ProfitTaxComputation WHERE CompanyId = @CompanyId AND Year = @Year AND Quarter = @Quarter);
    SELECT c.Id, c.Year, c.Quarter, c.Regime, c.TaxRate, c.TaxDue, c.EntryId, e.Number AS EntryNumber, c.PostedAmount, c.CalculatedAt, c.PostedAt,
        ISNULL((SELECT CHECKSUM_AGG(CHECKSUM(RowCode, Amount)) FROM fin.ProfitTaxComputationLine WHERE ComputationId = c.Id), 0) AS Hash
    FROM fin.ProfitTaxComputation AS c LEFT JOIN fin.JournalEntry AS e ON e.Id = c.EntryId WHERE c.Id = @Id;
    SELECT RowCode, Label, SortOrder, IsManual, Amount, Reason FROM fin.ProfitTaxComputationLine WHERE ComputationId = @Id ORDER BY SortOrder;
END;
