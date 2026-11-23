CREATE OR ALTER PROCEDURE core.usp_Attachment_List
    @CompanyId  int = NULL,
    @EntityType varchar(60),
    @EntityId   bigint
AS
BEGIN
    -- Atasamentele unei entitati (02.01), fara continut, cele mai noi intai.
    SET NOCOUNT ON;

    SELECT a.Id, a.EntityType, a.EntityId, a.FileName, a.ContentType, a.Size, a.Sha256, a.CreatedAt, u.DisplayName AS CreatedByName
    FROM core.Attachment AS a
    LEFT JOIN core.[User] AS u ON u.Id = a.CreatedBy
    WHERE a.EntityType = @EntityType AND a.EntityId = @EntityId AND (a.CompanyId IS NULL OR a.CompanyId = @CompanyId)
    ORDER BY a.Id DESC;
END;
