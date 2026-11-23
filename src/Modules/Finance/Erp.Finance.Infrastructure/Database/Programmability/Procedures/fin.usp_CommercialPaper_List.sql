CREATE OR ALTER PROCEDURE fin.usp_CommercialPaper_List
    @CompanyId    int,
    @Direction    tinyint = NULL,
    @ActiveOnly   bit = 0,
    @MaturityTo   date = NULL,
    @Year         int = NULL
AS
BEGIN
    -- Portofoliul de efecte (macheta f3-06): pe directie, active (in portofoliu sau depuse), scadente pana la o data, pe anul emiterii;
    -- intai cele cu scadenta cea mai apropiata. Al doilea set: totalurile cartonaselor (in portofoliu, depuse, emise de platit, refuzate in an).
    SET NOCOUNT ON;

    SELECT TOP (500) c.Id, c.PartnerId, p.Name AS PartnerName, c.Direction, c.Kind, c.Series, c.Number, c.Amount, c.CurrencyCode, c.IssueDate, c.MaturityDate,
        c.Status, ta.Code AS TreasuryAccountCode
    FROM fin.CommercialPaper AS c
    JOIN core.Partner AS p ON p.Id = c.PartnerId
    LEFT JOIN fin.TreasuryAccount AS ta ON ta.Id = c.TreasuryAccountId
    WHERE c.CompanyId = @CompanyId AND (@Direction IS NULL OR c.Direction = @Direction)
      AND (@ActiveOnly = 0 OR c.Status IN (1, 2)) AND (@MaturityTo IS NULL OR c.MaturityDate <= @MaturityTo)
      AND (@Year IS NULL OR YEAR(c.IssueDate) = @Year)
    ORDER BY CASE WHEN c.Status IN (1, 2) THEN 0 ELSE 1 END, c.MaturityDate, c.Id;

    SELECT ISNULL(SUM(CASE WHEN Direction = 1 AND Status = 1 THEN Amount END), 0) AS InPortfolio,
        SUM(CASE WHEN Direction = 1 AND Status = 1 THEN 1 ELSE 0 END) AS InPortfolioCount,
        ISNULL(SUM(CASE WHEN Direction = 1 AND Status = 2 THEN Amount END), 0) AS Deposited,
        SUM(CASE WHEN Direction = 1 AND Status = 2 THEN 1 ELSE 0 END) AS DepositedCount,
        ISNULL(SUM(CASE WHEN Direction = 2 AND Status = 1 THEN Amount END), 0) AS IssuedToPay,
        SUM(CASE WHEN Direction = 2 AND Status = 1 THEN 1 ELSE 0 END) AS IssuedToPayCount,
        ISNULL(SUM(CASE WHEN Status = 4 AND YEAR(MaturityDate) = ISNULL(@Year, YEAR(SYSUTCDATETIME())) THEN Amount END), 0) AS Refused,
        SUM(CASE WHEN Status = 4 AND YEAR(MaturityDate) = ISNULL(@Year, YEAR(SYSUTCDATETIME())) THEN 1 ELSE 0 END) AS RefusedCount
    FROM fin.CommercialPaper WHERE CompanyId = @CompanyId AND CurrencyCode = 'RON';
END;
