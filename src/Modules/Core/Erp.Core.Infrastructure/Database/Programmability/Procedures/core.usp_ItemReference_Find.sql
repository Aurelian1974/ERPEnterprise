CREATE OR ALTER PROCEDURE core.usp_ItemReference_Find
    @PartnerId     int,
    @ReferenceType tinyint,
    @Code          nvarchar(50) = NULL,
    @ItemId        int = NULL
AS
BEGIN
    -- Pentru documente (IItemCatalog): articolul dupa codul partenerului (@Code) sau codul partenerului pentru articol (@ItemId).
    -- Doar codurile active; la mai multe coduri ale aceluiasi articol, primul creat.
    SET NOCOUNT ON;

    SELECT TOP (1) r.ItemId, r.PartnerId, r.ReferenceType, r.Code, r.Description, r.UomId
    FROM core.ItemPartnerReference AS r
    WHERE r.PartnerId = @PartnerId AND r.ReferenceType = @ReferenceType AND r.IsActive = 1
      AND ((@Code IS NOT NULL AND r.CodeKey = UPPER(@Code)) OR (@Code IS NULL AND r.ItemId = @ItemId))
    ORDER BY r.Id;
END;
