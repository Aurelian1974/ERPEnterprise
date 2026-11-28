CREATE OR ALTER PROCEDURE inv.usp_Serial_Get
    @CompanyId int,
    @SerialId  int
AS
BEGIN
    -- Fisa seriei (02.05, S4 10; macheta s4-04): bucata, identificatorii ei, istoricul corectarilor si toate miscarile.
    SET NOCOUNT ON;

    SELECT s.Id AS SerialId, s.SerialNo, s.ItemVariantId, v.Code AS VariantCode, v.Name AS VariantName, s.Status, s.WarehouseId, w.Code AS WarehouseCode,
        s.LotId, l.LotCode, s.SoldAt, s.SoldToPartnerId, p.Name AS SoldToName, s.WarrantyUntil, s.IsCapturedAtIssue
    FROM inv.Serial AS s
    JOIN core.ItemVariant AS v ON v.Id = s.ItemVariantId
    LEFT JOIN inv.Warehouse AS w ON w.Id = s.WarehouseId
    LEFT JOIN inv.Lot AS l ON l.Id = s.LotId
    LEFT JOIN core.Partner AS p ON p.Id = s.SoldToPartnerId
    WHERE s.Id = @SerialId AND s.CompanyId = @CompanyId;

    SELECT t.Code AS TypeCode, t.Name AS TypeName, i.Value
    FROM inv.SerialIdentifier AS i JOIN inv.SerialIdentifierType AS t ON t.Id = i.IdentifierTypeId
    JOIN inv.Serial AS s ON s.Id = i.SerialId AND s.CompanyId = @CompanyId
    WHERE i.SerialId = @SerialId
    ORDER BY t.Code;

    SELECT m.Id AS MovementId, m.MovementDate, t.Code AS MovementTypeCode, w.Code AS WarehouseCode, m.QtyBase, m.SourceDocumentType, m.SourceDocumentNumber,
        m.PartnerId
    FROM inv.StockMovement AS m
    JOIN inv.MovementType AS t ON t.Id = m.MovementTypeId
    JOIN inv.Warehouse AS w ON w.Id = m.WarehouseId
    WHERE m.SerialId = @SerialId AND m.CompanyId = @CompanyId
    ORDER BY m.PostingSeq;
END;
