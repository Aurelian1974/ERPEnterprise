CREATE OR ALTER PROCEDURE inv.usp_Warehouse_Lookups
    @CompanyId int
AS
BEGIN
    -- Listele fisei gestiunii (macheta s1-01): locatiile active ale firmei, centrele de cost active, gestiunile de retur de la clienti.
    SET NOCOUNT ON;

    SELECT Id, Code, Name, LocationType FROM core.Location WHERE CompanyId = @CompanyId AND IsActive = 1 ORDER BY CodeKey;

    SELECT Id, Code, Name FROM core.DimensionValue
    WHERE CompanyId = @CompanyId AND DimensionType = 'COST_CENTER' AND IsActive = 1
    ORDER BY CodeKey;

    SELECT Id, Code, Name FROM inv.Warehouse
    WHERE CompanyId = @CompanyId AND Kind = 1 AND Purpose = 2 AND IsActive = 1
    ORDER BY CodeKey;
END;
