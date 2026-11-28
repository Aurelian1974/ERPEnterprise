CREATE OR ALTER PROCEDURE inv.usp_StockSettings_Save
    @CompanyId                  int,
    @DefaultValuationMethod     tinyint,
    @DefaultNegativeStockPolicy tinyint,
    @RequireCountOnHandover     bit,
    @BackdatingDays             smallint,
    @BackdatingWithRight        bit,
    @TransferMode               tinyint,
    @CostRecalcScope            tinyint,
    @LandedCostAllocation       tinyint,
    @ExpiryPickRule             tinyint,
    @ExpiryWarningDays          smallint,
    @ReservationExpiryDays      smallint,
    @CountFreezeMode            tinyint,
    @RowVersion                 binary(8),
    @UserId                     int
AS
BEGIN
    -- Setarile de stoc ale firmei (02.05, S1 3.3; Configurare › Stocuri, S7). Nivelul de evidenta se schimba separat (inv.usp_StockLevel_Set,
    -- cu verificarea stocului zero). Versiune diferita: 54014.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE inv.StockSettings
    SET DefaultValuationMethod = @DefaultValuationMethod, DefaultNegativeStockPolicy = @DefaultNegativeStockPolicy,
        RequireCountOnHandover = @RequireCountOnHandover, BackdatingDays = @BackdatingDays, BackdatingWithRight = @BackdatingWithRight,
        TransferMode = @TransferMode, CostRecalcScope = @CostRecalcScope, LandedCostAllocation = @LandedCostAllocation,
        ExpiryPickRule = @ExpiryPickRule, ExpiryWarningDays = @ExpiryWarningDays, ReservationExpiryDays = @ReservationExpiryDays,
        CountFreezeMode = @CountFreezeMode, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE CompanyId = @CompanyId AND RowVersion = @RowVersion;

    IF @@ROWCOUNT = 0
        THROW 54014, N'Setările de stoc au fost modificate de alt utilizator; reîncărcați datele.', 1;

    SELECT Id, RowVersion FROM inv.StockSettings WHERE CompanyId = @CompanyId;
END;
