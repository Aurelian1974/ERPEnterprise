CREATE OR ALTER PROCEDURE fin.usp_Intercompany_Differences
    @ChartId  int,
    @Year     smallint,
    @Month    tinyint
AS
BEGIN
    -- Diferentele intra-grup (C5 3.7): pe fiecare pereche de conturi si pereche de firme, soldul debitor al primului cont la firma A cu partenerul
    -- firmei B fata de soldul creditor al celui de-al doilea cont la firma B cu partenerul firmei A (factura in tranzit, curs diferit); nu se ajusteaza.
    SET NOCOUNT ON;
    WITH Lines AS (
        SELECT m.CompanyId, m.TargetAccountId AS AccountId, COALESCE(p.IntraGroupCompanyId, eg.GroupCompanyId) AS PartnerCompanyId,
            SUM(CASE WHEN m.Side = 1 THEN m.AmountRon ELSE -m.AmountRon END) AS Net
        FROM fin.JournalLineAltAccount AS m
        JOIN fin.JournalLine AS l ON l.Id = m.JournalLineId
        JOIN fin.JournalEntry AS e ON e.Id = l.EntryId AND e.Status >= 2
        LEFT JOIN core.Partner AS p ON p.Id = l.PartnerId
        OUTER APPLY (SELECT MAX(p2.IntraGroupCompanyId) AS GroupCompanyId FROM fin.JournalLine AS l2 JOIN core.Partner AS p2 ON p2.Id = l2.PartnerId
                     WHERE l2.EntryId = l.EntryId) AS eg
        WHERE m.ChartId = @ChartId AND m.PeriodYear = @Year AND m.PeriodMonth <= @Month
          AND m.CompanyId IN (SELECT CompanyId FROM fin.ChartOfAccountsCompany WHERE ChartId = @ChartId)
          AND COALESCE(p.IntraGroupCompanyId, eg.GroupCompanyId) IS NOT NULL
        GROUP BY m.CompanyId, m.TargetAccountId, COALESCE(p.IntraGroupCompanyId, eg.GroupCompanyId)
    ), Firms AS (SELECT a.CompanyId AS A, b.CompanyId AS B FROM fin.ChartOfAccountsCompany AS a JOIN fin.ChartOfAccountsCompany AS b ON b.ChartId = a.ChartId AND b.CompanyId <> a.CompanyId
                 WHERE a.ChartId = @ChartId)
    SELECT pr.Name AS PairName, f.A AS FirstCompanyId, ca.Code AS FirstCompanyCode, f.B AS SecondCompanyId, cb.Code AS SecondCompanyCode,
        ISNULL(x.Net, 0) AS FirstAmount, -ISNULL(y.Net, 0) AS SecondAmount, ISNULL(x.Net, 0) + ISNULL(y.Net, 0) AS Difference
    FROM fin.IntercompanyPair AS pr
    CROSS JOIN Firms AS f
    JOIN core.Company AS ca ON ca.Id = f.A
    JOIN core.Company AS cb ON cb.Id = f.B
    LEFT JOIN Lines AS x ON x.CompanyId = f.A AND x.AccountId = pr.FirstAccountId AND x.PartnerCompanyId = f.B
    LEFT JOIN Lines AS y ON y.CompanyId = f.B AND y.AccountId = pr.SecondAccountId AND y.PartnerCompanyId = f.A
    WHERE pr.ChartId = @ChartId AND (x.Net IS NOT NULL OR y.Net IS NOT NULL)
    ORDER BY pr.Name, ca.Code, cb.Code;
END;
