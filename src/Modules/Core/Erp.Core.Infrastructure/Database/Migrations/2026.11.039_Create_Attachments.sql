-- Atasamentele (02.01, 3 „Attachment”, 6 IAttachmentStore; SEC-05): fisiere legate generic de orice entitate din orice modul (EntityType +
-- EntityId), pe firma. Hash-ul SHA-256 garanteaza integritatea fisierelor fiscale. StorageKind: 1 in baza de date (Content), 2 pe disc
-- (StoragePath, numele generat de sistem); faza 1 foloseste baza de date (Attachments:StorageKind = Database). Nu se sterg (pastrare fiscala).
CREATE TABLE core.Attachment (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Attachment PRIMARY KEY,
    CompanyId    int               NULL CONSTRAINT FK_Attachment_Company REFERENCES core.Company (Id),
    EntityType   varchar(60)       NOT NULL,
    EntityId     bigint            NOT NULL,
    FileName     nvarchar(255)     NOT NULL,
    ContentType  varchar(100)      NOT NULL,
    Size         bigint            NOT NULL,
    Sha256       binary(32)        NOT NULL,
    StorageKind  tinyint           NOT NULL CONSTRAINT DF_Attachment_StorageKind DEFAULT (1),
    StoragePath  nvarchar(400)     NULL,
    Content      varbinary(max)    NULL,
    CreatedAt    datetime2(3)      NOT NULL CONSTRAINT DF_Attachment_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy    int               NOT NULL,
    CONSTRAINT CK_Attachment_Size CHECK (Size > 0),
    CONSTRAINT CK_Attachment_Storage CHECK ((StorageKind = 1 AND Content IS NOT NULL) OR (StorageKind = 2 AND StoragePath IS NOT NULL))
);
CREATE INDEX IX_Attachment_EntityType_EntityId ON core.Attachment (EntityType, EntityId) INCLUDE (CompanyId, FileName);
