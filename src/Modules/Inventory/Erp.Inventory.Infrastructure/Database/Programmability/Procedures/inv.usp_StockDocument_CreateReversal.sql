CREATE OR ALTER PROCEDURE inv.usp_StockDocument_CreateReversal
    @CompanyId    int,
    @Id           int,
    @DocumentDate date,
    @RowVersion   binary(8),
    @UserId       int
AS
BEGIN
    -- Documentul de stornare (02.05, S2 4.6): acelasi tip, aceleasi gestiuni, cantitatile cu semn opus, legat de original. Doar un document
    -- validat, nestornat si care nu e el insusi o stornare (54114). Intoarce documentul nou; apelantul aloca numarul si storneaza miscarile.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @IsReversal bit, @Version binary(8);
    SELECT @Status = Status, @IsReversal = IsReversal, @Version = RowVersion FROM inv.StockDocument WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 54001, N'Documentul nu există în firma curentă.', 1;
    IF @Version <> @RowVersion
        THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF EXISTS (SELECT 1 FROM inv.StockDocument WHERE Id = @Id AND DocumentType = 'NR')
        THROW 54114, N'Nota de primire nu se stornează; faceți o notă de predare inversă.', 1;
    IF @Status <> 2 OR @IsReversal = 1
        THROW 54114, N'Se stornează doar un document validat, nestornat, care nu e el însuși o stornare.', 1;
    IF EXISTS (SELECT 1 FROM inv.TransitShipment WHERE NpDocumentId = @Id AND Status <> 1)
        THROW 54114, N'Nota de predare are primiri; faceți o notă de predare inversă.', 1;

    INSERT INTO inv.StockDocument (CompanyId, DocumentType, DocumentDate, AccountingDate, FromDepotId, FromWarehouseId, ToDepotId, ToWarehouseId,
        ReasonId, PartnerId, CostCenterId, ProjectId, EmployeeId, TransferMode, RelatedDocumentId, IsReversal, Notes, CreatedBy)
    SELECT CompanyId, DocumentType, @DocumentDate, @DocumentDate, FromDepotId, FromWarehouseId, ToDepotId, ToWarehouseId, ReasonId, PartnerId,
        CostCenterId, ProjectId, EmployeeId, TransferMode, Id, 1, Notes, @UserId
    FROM inv.StockDocument WHERE Id = @Id;
    DECLARE @NewId int = SCOPE_IDENTITY();

    INSERT INTO inv.StockDocumentLine (DocumentId, LineNumber, ItemVariantId, ItemNameSnapshot, UomId, Qty, UomFactor, QtyBase, UnitCost, LotId, FromDepotId,
        FromWarehouseId, ToDepotId, ToWarehouseId, CostCenterId, ProjectId, ReversedLineId)
    SELECT @NewId, LineNumber, ItemVariantId, ItemNameSnapshot, UomId, -Qty, UomFactor, -QtyBase, UnitCost, LotId, FromDepotId, FromWarehouseId, ToDepotId,
        ToWarehouseId, CostCenterId, ProjectId, Id
    FROM inv.StockDocumentLine WHERE DocumentId = @Id;

    SELECT @NewId AS Id;
END;
