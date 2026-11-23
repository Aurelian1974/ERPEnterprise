CREATE OR ALTER PROCEDURE core.usp_Attachment_Save
    @CompanyId   int = NULL,
    @EntityType  varchar(60),
    @EntityId    bigint,
    @FileName    nvarchar(255),
    @ContentType varchar(100),
    @Size        bigint,
    @Sha256      binary(32),
    @Content     varbinary(max),
    @UserId      int
AS
BEGIN
    -- Salveaza un atasament in baza de date (02.01, IAttachmentStore.SaveAsync), in tranzactia apelantului; verificarile de tip si dimensiune
    -- (SEC-05) le face serviciul. Intoarce Id-ul.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    INSERT INTO core.Attachment (CompanyId, EntityType, EntityId, FileName, ContentType, Size, Sha256, StorageKind, Content, CreatedBy)
    VALUES (@CompanyId, @EntityType, @EntityId, @FileName, @ContentType, @Size, @Sha256, 1, @Content, @UserId);
    SELECT CAST(SCOPE_IDENTITY() AS int) AS Id;
END;
