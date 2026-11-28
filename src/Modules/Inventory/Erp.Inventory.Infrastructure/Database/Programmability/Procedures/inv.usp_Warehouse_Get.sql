CREATE OR ALTER PROCEDURE inv.usp_Warehouse_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Fisa gestiunii (02.05, S1 3.1; macheta s1-01, tab-ul Date generale). Gestiunea altei firme nu intoarce nimic.
    SET NOCOUNT ON;

    SELECT w.Id, w.CompanyId, w.Code, w.Name, w.LocationId, l.Name AS LocationName, w.Kind, w.ValuationBasis, w.RetailPriceListId, w.IsOwnGoods,
        w.ThirdPartyId, p.Name AS ThirdPartyName, w.NegativeStockPolicy, w.DimensionValueId, w.DefaultCostCenterId, w.IsBlocked, w.BlockReason,
        w.BlockedAt, w.DeliveryOrderMode, w.ReturnWarehouseId, w.Purpose, w.IsActive, w.FirstMovementAt, w.CreatedAt, w.ModifiedAt,
        u.DisplayName AS ModifiedByName, w.RowVersion
    FROM inv.Warehouse AS w
    LEFT JOIN core.Location AS l ON l.Id = w.LocationId
    LEFT JOIN core.Partner AS p ON p.Id = w.ThirdPartyId
    LEFT JOIN core.[User] AS u ON u.Id = w.ModifiedBy
    WHERE w.Id = @Id AND w.CompanyId = @CompanyId;
END;
