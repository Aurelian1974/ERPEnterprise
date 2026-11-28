CREATE OR ALTER PROCEDURE inv.usp_Transfer_MarkShipped
    @CompanyId    int,
    @DocumentId   int,
    @TransferMode tinyint,
    @UserId       int
AS
BEGIN
    -- Dupa miscarile notei de predare (02.05, S2 4.2–4.3): etapele pe document; in doua etape, expeditia in tranzit (Trimis). Intoarce expeditia.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ShipmentId int;
    IF @TransferMode = 2
    BEGIN
        INSERT INTO inv.TransitShipment (CompanyId, NpDocumentId, FromWarehouseId, FromDepotId, ToWarehouseId, ToDepotId, ShippedDate, Status, CreatedBy)
        SELECT CompanyId, Id, FromWarehouseId, FromDepotId, ToWarehouseId, ToDepotId, DocumentDate, 1, @UserId
        FROM inv.StockDocument WHERE Id = @DocumentId AND CompanyId = @CompanyId;
        SET @ShipmentId = SCOPE_IDENTITY();
    END;

    UPDATE inv.StockDocument SET TransferMode = @TransferMode, TransitShipmentId = @ShipmentId WHERE Id = @DocumentId AND CompanyId = @CompanyId;

    SELECT @ShipmentId AS TransitShipmentId;
END;
