CREATE OR ALTER PROCEDURE inv.usp_StockSettings_Ensure
    @CompanyId               int,
    @TransitDimensionValueId int,
    @UserId                  int
AS
BEGIN
    -- Prima aparitie a unei firme in Stocuri (02.05, S1 3.3, 3.1 si 5.2): setarile de stoc cu valorile implicite si gestiunea TRANZIT (una pe
    -- firma, fara locatie, nedezactivabila). Idempotenta: ruleaza la FirmaModificata si inaintea primei gestiuni; nu modifica nimic existent.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.StockSettings WITH (UPDLOCK, HOLDLOCK) WHERE CompanyId = @CompanyId)
        INSERT INTO inv.StockSettings (CompanyId, CreatedBy) VALUES (@CompanyId, @UserId);

    IF NOT EXISTS (SELECT 1 FROM inv.Warehouse WITH (UPDLOCK, HOLDLOCK) WHERE CompanyId = @CompanyId AND Kind = 3)
        INSERT INTO inv.Warehouse (CompanyId, Code, Name, Kind, ValuationBasis, IsOwnGoods, NegativeStockPolicy, DimensionValueId, CreatedBy)
        VALUES (@CompanyId, N'TRANZIT', N'Tranzit', 3, 1, 1, 1, @TransitDimensionValueId, @UserId);
END;
