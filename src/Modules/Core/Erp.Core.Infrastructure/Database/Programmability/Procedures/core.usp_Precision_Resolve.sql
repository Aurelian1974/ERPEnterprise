CREATE OR ALTER PROCEDURE core.usp_Precision_Resolve
    @CompanyId int,
    @ItemId    int = NULL
AS
BEGIN
    -- Precizia pentru un document (02.01, 3.3 si 3.5; P7): zecimalele proprii ale articolului au prioritate, apoi politica firmei.
    -- Listele de preturi (primul nivel al ierarhiei) se adauga odata cu Vanzarile. Firma inexistenta: 50021.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId)
        THROW 50021, N'Firma nu există.', 1;

    SELECT COALESCE(i.PricePrecision, p.PricePrecision) AS PriceDecimals,
           COALESCE(i.QuantityPrecision, p.QuantityPrecision) AS QuantityDecimals,
           p.AmountPrecision AS AmountDecimals,
           p.RoundingMode
    FROM core.Company AS c
    JOIN core.PrecisionPolicy AS p ON p.Id = c.DefaultPrecisionPolicyId
    LEFT JOIN core.Item AS i ON i.Id = @ItemId
    WHERE c.Id = @CompanyId;
END;
