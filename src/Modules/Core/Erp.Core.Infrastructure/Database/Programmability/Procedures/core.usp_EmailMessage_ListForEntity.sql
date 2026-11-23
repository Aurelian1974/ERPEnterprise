CREATE OR ALTER PROCEDURE core.usp_EmailMessage_ListForEntity
    @CompanyId  int = NULL,
    @EntityType varchar(60),
    @EntityId   bigint
AS
BEGIN
    -- Mesajele trimise pentru o entitate (de exemplu o confirmare de sold), cu starea lor, cele mai noi intai.
    SET NOCOUNT ON;

    SELECT m.Id, m.ToAddress, m.Subject, m.Status, m.Attempts, m.LastError, m.SentAt, m.CreatedAt
    FROM core.EmailMessage AS m
    WHERE m.EntityType = @EntityType AND m.EntityId = @EntityId AND (m.CompanyId IS NULL OR m.CompanyId = @CompanyId)
    ORDER BY m.Id DESC;
END;
