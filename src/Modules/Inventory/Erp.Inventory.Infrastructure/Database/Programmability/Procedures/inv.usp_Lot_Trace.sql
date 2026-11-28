CREATE OR ALTER PROCEDURE inv.usp_Lot_Trace
    @CompanyId int,
    @LotId     int,
    @MaxDepth  int = 10
AS
BEGIN
    -- Trasabilitatea unui lot (02.05, S4 3.6; macheta s4-05), recursiv prin genealogie, cu limita de adancime 10 (abatere SP-08, CTE recursiv):
    --   1. lotul; 2. inapoi: loturile din care provine (materii prime), cu furnizorul si prima receptie; 3. inainte: miscarile lotului si ale
    --   loturilor rezultate (clientii, transferurile, consumurile); 4. acum: stocul pe gestiuni, inclusiv tranzitul.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.Lot WHERE Id = @LotId AND CompanyId = @CompanyId)
        THROW 54454, N'Lotul nu există în firma curentă.', 1;

    SELECT x.Id AS LotId, x.CkuCode, x.LotCode, x.SupplierLotCode, x.ExpiryDate, x.Status, x.SupplierPartnerId, p.Name AS SupplierName, x.FirstReceiptDate,
        m.SourceDocumentType AS ReceiptDocumentType, m.SourceDocumentNumber AS ReceiptDocumentNumber, m.QtyBase AS ReceiptQty
    FROM inv.Lot AS x
    LEFT JOIN core.Partner AS p ON p.Id = x.SupplierPartnerId
    LEFT JOIN inv.StockMovement AS m ON m.Id = x.FirstReceiptMovementId
    WHERE x.Id = @LotId;

    WITH Back AS (
        SELECT g.ParentLotId AS LotId, 1 AS Depth FROM inv.LotGenealogy AS g WHERE g.ChildLotId = @LotId
        UNION ALL
        SELECT g.ParentLotId, b.Depth + 1 FROM inv.LotGenealogy AS g JOIN Back AS b ON g.ChildLotId = b.LotId WHERE b.Depth < @MaxDepth
    )
    SELECT DISTINCT b.Depth, x.Id AS LotId, x.CkuCode, x.SupplierLotCode, p.Name AS SupplierName, x.FirstReceiptDate
    FROM Back AS b JOIN inv.Lot AS x ON x.Id = b.LotId LEFT JOIN core.Partner AS p ON p.Id = x.SupplierPartnerId
    ORDER BY b.Depth
    OPTION (MAXRECURSION 10);

    DECLARE @Forward TABLE (LotId int PRIMARY KEY, Depth int);
    WITH Fwd AS (
        SELECT @LotId AS LotId, 0 AS Depth
        UNION ALL
        SELECT g.ChildLotId, f.Depth + 1 FROM inv.LotGenealogy AS g JOIN Fwd AS f ON g.ParentLotId = f.LotId WHERE f.Depth < @MaxDepth
    )
    INSERT INTO @Forward SELECT LotId, MIN(Depth) FROM Fwd GROUP BY LotId OPTION (MAXRECURSION 10);

    SELECT f.Depth, m.Id AS MovementId, m.LotId, x.CkuCode, m.MovementDate, t.Code AS MovementTypeCode, w.Code AS WarehouseCode, m.QtyBase, m.PartnerId,
        p.Name AS PartnerName, m.SourceDocumentType, m.SourceDocumentId, m.SourceDocumentNumber
    FROM @Forward AS f
    JOIN inv.StockMovement AS m ON m.LotId = f.LotId
    JOIN inv.Lot AS x ON x.Id = m.LotId
    JOIN inv.MovementType AS t ON t.Id = m.MovementTypeId
    JOIN inv.Warehouse AS w ON w.Id = m.WarehouseId
    LEFT JOIN core.Partner AS p ON p.Id = m.PartnerId
    ORDER BY m.MovementDate, m.PostingSeq;

    SELECT s.WarehouseId, w.Code AS WarehouseCode, w.Kind AS WarehouseKind, SUM(s.QtyOnHand) AS QtyOnHand
    FROM inv.Stock AS s JOIN inv.Warehouse AS w ON w.Id = s.WarehouseId
    WHERE s.LotId = @LotId AND s.QtyOnHand <> 0
    GROUP BY s.WarehouseId, w.Code, w.Kind
    ORDER BY w.Code;
END;
