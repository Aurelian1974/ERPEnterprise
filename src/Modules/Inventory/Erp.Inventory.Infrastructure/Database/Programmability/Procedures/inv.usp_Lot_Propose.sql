CREATE OR ALTER PROCEDURE inv.usp_Lot_Propose
    @CompanyId     int,
    @WarehouseId   int,
    @DepotId       int = NULL,
    @ItemVariantId int,
    @PartnerId     int = NULL,
    @OnDate        date
AS
BEGIN
    -- Propunerea de loturi (02.05, S4 3.5, 6; portul ProposeLotsAsync, macheta s4-03): loturile cheii cu disponibilul la data livrarii, durata
    -- minima a clientului si regula de alegere. Ordinea si excluderile le aplica apelantul (LotAllocator), ca la validare.
    SET NOCOUNT ON;

    SELECT @WarehouseId AS WarehouseId, @DepotId AS DepotId, @ItemVariantId AS ItemVariantId, x.Id AS LotId, x.CkuCode, x.LotCode, x.Status, x.ExpiryDate,
        x.ExpiryKind, x.FirstReceiptDate, x.IsTechnical, a.Available
    FROM inv.ufn_LotAvailability(@WarehouseId, ISNULL(@DepotId, 0), @ItemVariantId, @OnDate) AS a
    JOIN inv.Lot AS x ON x.Id = a.LotId AND x.CompanyId = @CompanyId
    WHERE a.Available > 0;

    SELECT COALESCE(pi.MinRemainingDays, pg.MinRemainingDays, s.MinRemainingShelfLifeDays, 0) AS MinRemainingDays,
        CAST(ISNULL(ss.ExpiryPickRule, 1) AS tinyint) AS PickRule
    FROM core.ItemVariant AS v
    JOIN core.Item AS i ON i.Id = v.ItemId
    LEFT JOIN inv.ItemStockSettings AS s ON s.CompanyId = @CompanyId AND s.ItemId = i.Id
    LEFT JOIN inv.StockSettings AS ss ON ss.CompanyId = @CompanyId
    LEFT JOIN inv.PartnerShelfLifeRule AS pi ON pi.CompanyId = @CompanyId AND pi.PartnerId = @PartnerId AND pi.ItemId = i.Id
    LEFT JOIN inv.PartnerShelfLifeRule AS pg ON pg.CompanyId = @CompanyId AND pg.PartnerId = @PartnerId AND pg.ItemGroupId = i.ItemGroupId
    WHERE v.Id = @ItemVariantId;
END;
