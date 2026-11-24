CREATE OR ALTER PROCEDURE fin.usp_FxExposure_Report
    @CompanyId        int,
    @AsOf             date,
    @IncludeAdvances  bit = 0
AS
BEGIN
    -- Pozitia valutara la o data (F4 10; macheta f4-03): elementele in valuta cu suma in moneda lor (cu semnul: active +, pasive −) si
    -- valoarea contabila in lei. Category: 1 conturi si case, 2 creante, 3 efecte comerciale, 4 datorii. Facturile in lei indexate intra in
    -- valuta de referinta. Avansurile doar cu @IncludeAdvances.
    SET NOCOUNT ON;

    DECLARE @Treasury TABLE (TreasuryAccountId int PRIMARY KEY, Code nvarchar(30), Kind tinyint, CurrencyCode char(3), GlAccountCode nvarchar(20),
                             Balance decimal(19,2), CarryingAmountRon decimal(19,2), MissingRates int);
    INSERT INTO @Treasury EXEC fin.usp_TreasuryFx_Values @CompanyId = @CompanyId, @AsOf = @AsOf;

    SELECT CAST(1 AS tinyint) AS Category, t.CurrencyCode, t.Code AS Name, CASE WHEN t.Kind = 2 THEN N'casă' ELSE N'cont bancar' END AS Description,
        CAST(NULL AS date) AS DueDate, t.Balance AS Amount, t.CarryingAmountRon
    FROM @Treasury AS t
    WHERE t.Balance <> 0
    UNION ALL
    SELECT CAST(CASE WHEN x.Kind = 8 THEN 3 WHEN x.ForeignAsOf > 0 THEN 2 ELSE 4 END AS tinyint), x.CurrencyCode, x.PartnerName, x.DocumentNo, x.DueDate,
        x.ForeignAsOf, x.Carrying
    FROM (SELECT o.Kind, COALESCE(o.IndexCurrencyCode, o.CurrencyCode) AS CurrencyCode, p.Name AS PartnerName, o.DocumentNo, o.DueDate,
                 CASE WHEN o.IndexCurrencyCode IS NULL THEN o.OpenAmount + ISNULL(d.Amount, 0) - ISNULL(c.Amount, 0)
                      ELSE ROUND(o.IndexedAmount * (o.OpenAmount + ISNULL(d.Amount, 0) - ISNULL(c.Amount, 0)) / o.Amount, 2) END AS ForeignAsOf,
                 o.OpenAmountRon + ISNULL(d.Ron, 0) - ISNULL(c.Ron, 0) AS Carrying
          FROM fin.OpenItem AS o
          JOIN core.Partner AS p ON p.Id = o.PartnerId
          OUTER APPLY (SELECT SUM(a.DebitAmount) AS Amount, SUM(a.AmountRonDebit) AS Ron FROM fin.Allocation AS a
                       WHERE a.DebitOpenItemId = o.Id AND a.Status = 1 AND a.AllocationDate > @AsOf) AS d
          OUTER APPLY (SELECT SUM(a.CreditAmount) AS Amount, SUM(a.AmountRonCredit) AS Ron FROM fin.Allocation AS a
                       WHERE a.CreditOpenItemId = o.Id AND a.Status = 1 AND a.AllocationDate > @AsOf) AS c
          WHERE o.CompanyId = @CompanyId AND (o.CurrencyCode <> 'RON' OR o.IndexCurrencyCode IS NOT NULL) AND o.DocumentDate <= @AsOf
            AND (o.IsMonetary = 1 OR @IncludeAdvances = 1)) AS x
    WHERE x.ForeignAsOf <> 0
    ORDER BY CurrencyCode, Category, Name;
END;
