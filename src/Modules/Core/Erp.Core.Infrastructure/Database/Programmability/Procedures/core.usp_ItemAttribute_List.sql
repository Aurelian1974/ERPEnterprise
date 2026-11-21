CREATE OR ALTER PROCEDURE core.usp_ItemAttribute_List
AS
BEGIN
    -- Atributele de varianta si valorile lor (02.01, 3.6 punctul 13), in ordinea lor.
    SET NOCOUNT ON;

    SELECT a.Id, a.Code, a.Name, a.SortOrder, a.IsActive, a.RowVersion
    FROM core.ItemAttribute AS a
    ORDER BY a.IsActive DESC, a.SortOrder, a.CodeKey;

    SELECT v.Id, v.AttributeId, v.Code, v.Name, v.SortOrder, v.IsActive
    FROM core.ItemAttributeValue AS v
    ORDER BY v.AttributeId, v.SortOrder, v.CodeKey;
END;
