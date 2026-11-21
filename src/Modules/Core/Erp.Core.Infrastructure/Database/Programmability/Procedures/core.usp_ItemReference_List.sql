CREATE OR ALTER PROCEDURE core.usp_ItemReference_List
    @ItemId int
AS
BEGIN
    -- Tab-ul „Coduri la parteneri” al articolului (02.01, 3.6 punctul 11): codul, denumirea si UM la fiecare client sau furnizor.
    SET NOCOUNT ON;

    SELECT r.Id, r.ItemId, r.PartnerId, p.Code AS PartnerCode, p.Name AS PartnerName, r.ReferenceType, r.Code, r.Description, r.UomId,
           r.IsActive, r.RowVersion
    FROM core.ItemPartnerReference AS r
    JOIN core.Partner AS p ON p.Id = r.PartnerId
    WHERE r.ItemId = @ItemId
    ORDER BY r.IsActive DESC, r.ReferenceType, p.NameSearch, r.CodeKey;
END;
