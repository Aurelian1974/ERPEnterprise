CREATE OR ALTER PROCEDURE fin.usp_Compensation_Proposals
    @CompanyId int
AS
BEGIN
    -- Propunerile de compensare (F3 4.4 pasul 1, macheta f3-05): partenerii care sunt si clienti, si furnizori, cu creante (facturi client
    -- deschise) si datorii (facturi furnizor deschise) in aceeasi moneda; suma propusa e minimul dintre ele. Fara partidele in litigiu.
    SET NOCOUNT ON;

    WITH Sides AS (
        SELECT o.PartnerId, o.CurrencyCode,
            SUM(CASE WHEN o.Role = 1 AND o.OpenAmount > 0 THEN o.OpenAmount ELSE 0 END) AS Receivable,
            -SUM(CASE WHEN o.Role = 2 AND o.OpenAmount < 0 THEN o.OpenAmount ELSE 0 END) AS Payable
        FROM fin.OpenItem AS o
        WHERE o.CompanyId = @CompanyId AND o.OpenAmount <> 0 AND o.IsDisputed = 0 AND o.Kind IN (1, 3, 4, 6)
        GROUP BY o.PartnerId, o.CurrencyCode)
    SELECT TOP (100) s.PartnerId, p.Name AS PartnerName, s.CurrencyCode, CASE WHEN s.Receivable < s.Payable THEN s.Receivable ELSE s.Payable END AS Amount
    FROM Sides AS s JOIN core.Partner AS p ON p.Id = s.PartnerId
    WHERE s.Receivable > 0 AND s.Payable > 0
    ORDER BY Amount DESC;
END;
