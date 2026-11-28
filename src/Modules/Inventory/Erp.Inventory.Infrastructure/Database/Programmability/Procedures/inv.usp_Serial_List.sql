CREATE OR ALTER PROCEDURE inv.usp_Serial_List
    @CompanyId     int,
    @ItemVariantId int = NULL,
    @WarehouseId   int = NULL,
    @Status        tinyint = NULL,
    @Search        nvarchar(100) = NULL,
    @Top           int = 500
AS
BEGIN
    -- Seriile unei variante sau gestiuni (02.05, S4 11; macheta s4-04): starea, gestiunea sau clientul, garantia; cautarea dupa serie sau dupa
    -- oricare identificator (fara majuscule si diacritice).
    SET NOCOUNT ON;

    SELECT TOP (@Top) s.Id AS SerialId, s.SerialNo, s.ItemVariantId, v.Code AS VariantCode, v.Name AS VariantName, s.Status, s.WarehouseId, w.Code AS WarehouseCode,
        s.LotId, l.LotCode, s.SoldAt, s.SoldToPartnerId, p.Name AS SoldToName, s.WarrantyUntil, s.IsCapturedAtIssue
    FROM inv.Serial AS s
    JOIN core.ItemVariant AS v ON v.Id = s.ItemVariantId
    LEFT JOIN inv.Warehouse AS w ON w.Id = s.WarehouseId
    LEFT JOIN inv.Lot AS l ON l.Id = s.LotId
    LEFT JOIN core.Partner AS p ON p.Id = s.SoldToPartnerId
    WHERE s.CompanyId = @CompanyId
      AND (@ItemVariantId IS NULL OR s.ItemVariantId = @ItemVariantId)
      AND (@WarehouseId IS NULL OR s.WarehouseId = @WarehouseId)
      AND (@Status IS NULL OR s.Status = @Status)
      AND (@Search IS NULL OR s.SerialNoSearch LIKE N'%' + @Search + N'%'
           OR EXISTS (SELECT 1 FROM inv.SerialIdentifier AS i WHERE i.SerialId = s.Id AND i.ValueSearch LIKE N'%' + @Search + N'%'))
    ORDER BY v.Code, s.SerialNo;
END;
