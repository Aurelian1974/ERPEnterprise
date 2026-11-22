CREATE OR ALTER PROCEDURE fin.usp_CashLimitRule_ListEffective
    @CompanyId int,
    @Day       date
AS
BEGIN
    -- Plafoanele de numerar in vigoare la @Day (02.02, F1 3.6; P6): pentru fiecare categorie, regula legala (CompanyId NULL) si regula
    -- proprie a firmei, fiecare cu ultimul ValidFrom anterior zilei. Se verifica amandoua; cea mai stricta castiga.
    SET NOCOUNT ON;

    SELECT r.Category, r.Amount, r.Action
    FROM (
        SELECT c.Category, c.Amount, c.Action,
               ROW_NUMBER() OVER (PARTITION BY c.CompanyId, c.Category, c.CurrencyCode ORDER BY c.ValidFrom DESC) AS Rn
        FROM fin.CashLimitRule AS c
        WHERE (c.CompanyId IS NULL OR c.CompanyId = @CompanyId) AND c.CurrencyCode = 'RON'
          AND c.ValidFrom <= @Day AND (c.ValidTo IS NULL OR c.ValidTo >= @Day)
    ) AS r
    WHERE r.Rn = 1;
END;
