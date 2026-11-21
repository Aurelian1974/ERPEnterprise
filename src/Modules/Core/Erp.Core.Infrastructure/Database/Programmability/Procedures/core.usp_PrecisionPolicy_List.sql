CREATE OR ALTER PROCEDURE core.usp_PrecisionPolicy_List
AS
BEGIN
    -- Configurare › Precizie (macheta 07d): politicile, apoi unde se aplica fiecare (firmele care o au implicita) si numarul
    -- articolelor cu zecimale proprii (nivelul „articol” din ierarhie; listele de preturi vin cu Vanzarile).
    SET NOCOUNT ON;

    SELECT p.Id, p.Code, p.Name, p.PricePrecision, p.QuantityPrecision, p.AmountPrecision, p.RoundingMode, p.IsActive,
           (SELECT COUNT(*) FROM core.Company AS c WHERE c.DefaultPrecisionPolicyId = p.Id) AS CompanyCount, p.RowVersion
    FROM core.PrecisionPolicy AS p
    ORDER BY p.IsActive DESC, p.Code;

    SELECT c.Id AS CompanyId, c.Name AS CompanyName, c.DefaultPrecisionPolicyId AS PolicyId
    FROM core.Company AS c
    WHERE c.IsActive = 1
    ORDER BY c.Name;

    SELECT COUNT(*) AS ItemOverrides
    FROM core.Item AS i
    WHERE i.IsActive = 1 AND (i.PricePrecision IS NOT NULL OR i.QuantityPrecision IS NOT NULL);
END;
