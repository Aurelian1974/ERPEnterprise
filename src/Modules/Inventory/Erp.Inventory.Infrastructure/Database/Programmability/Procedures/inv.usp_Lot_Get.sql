CREATE OR ALTER PROCEDURE inv.usp_Lot_Get
    @CompanyId int,
    @LotId     int = NULL,
    @CkuCode   nvarchar(120) = NULL,
    @CkuUid    uniqueidentifier = NULL,
    @ItemVariantId int = NULL,
    @LotCode   nvarchar(40) = NULL,
    @ExpiryDate date = NULL
AS
BEGIN
    -- Fisa lotului (02.05, S4 3.2, 6; macheta s4-01): dupa Id, dupa CKU (cod sau uid, scanarea etichetei interne) sau dupa varianta, cod si
    -- termen; istoricul starilor si stocul pe gestiuni.
    SET NOCOUNT ON;

    DECLARE @Id int = COALESCE(@LotId,
        (SELECT Id FROM inv.Lot WHERE CompanyId = @CompanyId AND (CkuUid = @CkuUid OR CkuCode = @CkuCode)),
        (SELECT TOP (1) Id FROM inv.Lot WHERE CompanyId = @CompanyId AND ItemVariantId = @ItemVariantId AND LotCode = @LotCode
            AND (@ExpiryDate IS NULL OR ExpiryDate = @ExpiryDate) ORDER BY ExpiryDate DESC));

    SELECT x.Id AS LotId, x.ItemVariantId, v.Code AS VariantCode, v.Name AS VariantName, x.LotCode, x.CkuCode, x.CkuUid, x.SupplierLotCode,
        x.SupplierPartnerId, p.Name AS SupplierName, x.ProductionDate, x.ExpiryDate, x.ExpiryKind, x.CountryOfOriginCode, x.Status, x.StatusReason,
        x.IsTechnical, x.FirstReceiptDate, x.RowVersion
    FROM inv.Lot AS x
    JOIN core.ItemVariant AS v ON v.Id = x.ItemVariantId
    LEFT JOIN core.Partner AS p ON p.Id = x.SupplierPartnerId
    WHERE x.Id = @Id AND x.CompanyId = @CompanyId;

    SELECT h.FromStatus, h.ToStatus, h.Reason, h.SourceDocumentType, h.SourceDocumentId, h.ChangedAt, u.DisplayName AS ChangedByName
    FROM inv.LotStatusHistory AS h LEFT JOIN core.[User] AS u ON u.Id = h.ChangedBy
    WHERE h.LotId = @Id
    ORDER BY h.ChangedAt DESC, h.Id DESC;

    SELECT s.WarehouseId, w.Code AS WarehouseCode, SUM(s.QtyOnHand) AS QtyOnHand
    FROM inv.Stock AS s JOIN inv.Warehouse AS w ON w.Id = s.WarehouseId
    WHERE s.LotId = @Id AND s.QtyOnHand <> 0
    GROUP BY s.WarehouseId, w.Code
    ORDER BY w.Code;
END;
