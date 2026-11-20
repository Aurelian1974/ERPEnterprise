CREATE OR ALTER PROCEDURE core.usp_ItemBarcode_Find
    @Keys core.PartnerKeyList READONLY
AS
BEGIN
    -- Codurile de bare existente dintre cele date (coloana Code a listei), cu articolul lor: la import, un cod de bare folosit de
    -- alt articol e eroare.
    SET NOCOUNT ON;

    SELECT b.Barcode, b.ItemId, i.Code AS ItemCode
    FROM core.ItemBarcode AS b
    JOIN core.Item AS i ON i.Id = b.ItemId
    JOIN (SELECT DISTINCT Code FROM @Keys WHERE Code IS NOT NULL) AS k ON k.Code = b.Barcode;
END;
