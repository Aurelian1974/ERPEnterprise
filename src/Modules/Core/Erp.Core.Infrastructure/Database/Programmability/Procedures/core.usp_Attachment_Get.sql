CREATE OR ALTER PROCEDURE core.usp_Attachment_Get
    @Id          int,
    @CompanyId   int = NULL,
    @WithContent bit = 0
AS
BEGIN
    -- Un atasament al firmei (sau fara firma), cu continutul la cerere (02.01, IAttachmentStore.OpenAsync).
    SET NOCOUNT ON;

    SELECT a.Id, a.EntityType, a.EntityId, a.FileName, a.ContentType, a.Size, a.Sha256, a.CreatedAt, u.DisplayName AS CreatedByName,
        CASE WHEN @WithContent = 1 THEN a.Content END AS Content
    FROM core.Attachment AS a
    LEFT JOIN core.[User] AS u ON u.Id = a.CreatedBy
    WHERE a.Id = @Id AND (a.CompanyId IS NULL OR a.CompanyId = @CompanyId);
END;
