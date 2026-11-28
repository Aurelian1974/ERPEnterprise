CREATE OR ALTER PROCEDURE inv.usp_Serial_Find
    @CompanyId     int = NULL,
    @ItemVariantId int = NULL,
    @Value         nvarchar(100),
    @TypeCode      nvarchar(20) = NULL,
    @Exact         bit = 1
AS
BEGIN
    -- Cautarea unei bucati (02.05, S4 3.4, 6; portul FindSerialAsync, FindSerialByIdentifierAsync): dupa serie (pe varianta sau pe toate) sau dupa
    -- oricare identificator (IMEI, MAC, VIN...), optional doar pe un tip; exact (scanare) sau fara majuscule si diacritice (cautare din ecran).
    SET NOCOUNT ON;

    DECLARE @Ids TABLE (Id int PRIMARY KEY);
    INSERT INTO @Ids
    SELECT s.Id FROM inv.Serial AS s
    WHERE @TypeCode IS NULL AND (@ItemVariantId IS NULL OR s.ItemVariantId = @ItemVariantId)
      AND ((@Exact = 1 AND s.SerialNo = @Value) OR (@Exact = 0 AND s.SerialNoSearch = @Value))
    UNION
    SELECT i.SerialId FROM inv.SerialIdentifier AS i
    JOIN inv.SerialIdentifierType AS t ON t.Id = i.IdentifierTypeId
    WHERE (@TypeCode IS NULL OR t.CodeKey = UPPER(@TypeCode))
      AND ((@Exact = 1 AND i.Value = @Value) OR (@Exact = 0 AND i.ValueSearch = @Value));

    SELECT s.Id AS SerialId, s.SerialNo, s.ItemVariantId, s.CompanyId, s.Status, s.WarehouseId, s.WarrantyUntil
    FROM inv.Serial AS s JOIN @Ids AS x ON x.Id = s.Id
    WHERE (@CompanyId IS NULL OR s.CompanyId = @CompanyId) AND (@ItemVariantId IS NULL OR s.ItemVariantId = @ItemVariantId)
    ORDER BY s.Id;
END;
