CREATE OR ALTER PROCEDURE core.usp_EmailMessage_Create
    @CompanyId     int = NULL,
    @ToAddress     nvarchar(320),
    @CcAddress     nvarchar(320) = NULL,
    @Subject       nvarchar(300),
    @Body          nvarchar(max),
    @IsHtml        bit,
    @EntityType    varchar(60) = NULL,
    @EntityId      bigint = NULL,
    @AttachmentIds nvarchar(max) = NULL,
    @UserId        int
AS
BEGIN
    -- Un mesaj de email de trimis (02.01, 4.7), in tranzactia apelantului; atasamentele sunt ale firmei (50040 altfel). Intoarce Id-ul.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Attachments TABLE (Id int PRIMARY KEY);
    INSERT INTO @Attachments (Id) SELECT DISTINCT CAST(j.value AS int) FROM OPENJSON(ISNULL(@AttachmentIds, N'[]')) AS j;
    IF EXISTS (SELECT 1 FROM @Attachments AS x LEFT JOIN core.Attachment AS a ON a.Id = x.Id AND (a.CompanyId IS NULL OR a.CompanyId = @CompanyId) WHERE a.Id IS NULL)
        THROW 50040, N'Un atașament al mesajului nu există.', 1;

    INSERT INTO core.EmailMessage (CompanyId, ToAddress, CcAddress, Subject, Body, IsHtml, EntityType, EntityId, CreatedBy)
    VALUES (@CompanyId, @ToAddress, @CcAddress, @Subject, @Body, @IsHtml, @EntityType, @EntityId, @UserId);
    DECLARE @Id int = SCOPE_IDENTITY();
    INSERT INTO core.EmailMessageAttachment (EmailMessageId, AttachmentId) SELECT @Id, Id FROM @Attachments;
    SELECT @Id;
END;
