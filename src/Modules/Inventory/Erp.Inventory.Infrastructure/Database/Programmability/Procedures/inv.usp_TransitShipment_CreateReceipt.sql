CREATE OR ALTER PROCEDURE inv.usp_TransitShipment_CreateReceipt
    @CompanyId    int,
    @ShipmentId   int,
    @DocumentDate date,
    @UserId       int
AS
BEGIN
    -- Nota de primire a unei expeditii (02.05, S2 4.3 pasii 2–3): ciorna din tranzit in gestiunea care primeste, cu cantitatile ramase in tranzit
    -- pe fiecare linie a notei de predare, propuse ca primite. Expeditia inchisa sau fara rest: 54114; o alta primire in ciorna: 54107.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @NpId int, @Status tinyint, @ToWarehouseId int, @ToDepotId int, @TransitId int;
    SELECT @NpId = NpDocumentId, @Status = Status, @ToWarehouseId = ToWarehouseId, @ToDepotId = ToDepotId
    FROM inv.TransitShipment WITH (UPDLOCK, ROWLOCK) WHERE Id = @ShipmentId AND CompanyId = @CompanyId;
    IF @NpId IS NULL
        THROW 54001, N'Expediția nu există în firma curentă.', 1;
    IF @Status NOT IN (1, 2)
        THROW 54114, N'Expediția a fost primită integral sau este închisă.', 1;
    IF EXISTS (SELECT 1 FROM inv.StockDocument WHERE TransitShipmentId = @ShipmentId AND DocumentType = 'NR' AND Status = 1)
        THROW 54107, N'Expediția are deja o notă de primire în ciornă.', 1;
    SET @TransitId = (SELECT Id FROM inv.Warehouse WHERE CompanyId = @CompanyId AND Kind = 3);

    DECLARE @Remaining TABLE (NpLineId bigint PRIMARY KEY, Qty decimal(19,6));
    INSERT INTO @Remaining
    SELECT np.Id, np.QtyBase - ISNULL((
        SELECT SUM(nr.ReceivedQtyBase + CASE WHEN nr.DifferenceResolution IN (2, 3, 4, 5) THEN nr.DifferenceQtyBase ELSE 0 END)
        FROM inv.StockDocumentLine AS nr JOIN inv.StockDocument AS d ON d.Id = nr.DocumentId
        WHERE nr.ShipmentLineId = np.Id AND d.Status = 2 AND d.IsReversal = 0), 0)
    FROM inv.StockDocumentLine AS np
    WHERE np.DocumentId = @NpId;
    IF NOT EXISTS (SELECT 1 FROM @Remaining WHERE Qty > 0)
        THROW 54114, N'Expediția nu mai are cantități în tranzit.', 1;

    INSERT INTO inv.StockDocument (CompanyId, DocumentType, DocumentDate, AccountingDate, FromWarehouseId, ToWarehouseId, ToDepotId, TransferMode,
        TransitShipmentId, RelatedDocumentId, Notes, CreatedBy)
    VALUES (@CompanyId, 'NR', @DocumentDate, @DocumentDate, @TransitId, @ToWarehouseId, @ToDepotId, 2, @ShipmentId, @NpId, NULL, @UserId);
    DECLARE @Id int = SCOPE_IDENTITY();

    INSERT INTO inv.StockDocumentLine (DocumentId, LineNumber, ItemVariantId, ItemNameSnapshot, UomId, Qty, UomFactor, QtyBase, UnitCost, FromWarehouseId,
        ToWarehouseId, ToDepotId, CostCenterId, ProjectId, ReceivedQtyBase, DifferenceQtyBase, ShipmentLineId)
    SELECT @Id, ROW_NUMBER() OVER (ORDER BY np.LineNumber), np.ItemVariantId, np.ItemNameSnapshot, NULL, r.Qty, 1, r.Qty, np.UnitCostPosted, @TransitId,
        ISNULL(np.ToWarehouseId, @ToWarehouseId), ISNULL(np.ToDepotId, @ToDepotId), np.CostCenterId, np.ProjectId, r.Qty, 0, np.Id
    FROM inv.StockDocumentLine AS np
    JOIN @Remaining AS r ON r.NpLineId = np.Id
    WHERE r.Qty > 0;

    SELECT @Id AS Id;
END;
