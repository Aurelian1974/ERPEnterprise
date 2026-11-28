CREATE OR ALTER PROCEDURE inv.usp_StockSettings_Get
    @CompanyId int
AS
BEGIN
    -- Setarile de stoc ale firmei (02.05, S1 3.3); niciun rand daca firma nu a fost inca initializata in Stocuri.
    SET NOCOUNT ON;

    SELECT DefaultValuationMethod, StockKeepingLevel, DefaultNegativeStockPolicy, RequireCountOnHandover, BackdatingDays, BackdatingWithRight,
        TransferMode, CostRecalcScope, LandedCostAllocation, ExpiryPickRule, ExpiryWarningDays, ReservationExpiryDays, CountFreezeMode, RowVersion
    FROM inv.StockSettings
    WHERE CompanyId = @CompanyId;
END;
