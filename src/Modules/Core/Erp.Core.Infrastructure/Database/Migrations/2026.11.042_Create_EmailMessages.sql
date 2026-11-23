-- Emailul trimis din aplicatie (02.01, 4.7): mesajul se scrie in aceeasi tranzactie cu operatia, impreuna cu evenimentul EmailCerut in
-- outbox; Worker-ul il trimite prin serverul SMTP al instalarii, cu reincercarile outbox-ului. Status: 1 De trimis, 2 Trimis, 3 Esuat.
-- Fisierele atasate sunt atasamente ale Nucleului (core.Attachment). Mesajul e legat optional de entitatea care l-a cerut.
CREATE TABLE core.EmailMessage (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailMessage PRIMARY KEY,
    CompanyId    int               NULL CONSTRAINT FK_EmailMessage_Company REFERENCES core.Company (Id),
    ToAddress    nvarchar(320)     NOT NULL,
    CcAddress    nvarchar(320)     NULL,
    Subject      nvarchar(300)     NOT NULL,
    Body         nvarchar(max)     NOT NULL,
    IsHtml       bit               NOT NULL CONSTRAINT DF_EmailMessage_IsHtml DEFAULT (0),
    EntityType   varchar(60)       NULL,
    EntityId     bigint            NULL,
    Status       tinyint           NOT NULL CONSTRAINT DF_EmailMessage_Status DEFAULT (1),
    Attempts     int               NOT NULL CONSTRAINT DF_EmailMessage_Attempts DEFAULT (0),
    LastError    nvarchar(1000)    NULL,
    SentAt       datetime2(3)      NULL,
    CreatedAt    datetime2(3)      NOT NULL CONSTRAINT DF_EmailMessage_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy    int               NOT NULL,
    CONSTRAINT CK_EmailMessage_Status CHECK (Status IN (1, 2, 3))
);
CREATE INDEX IX_EmailMessage_EntityType_EntityId ON core.EmailMessage (EntityType, EntityId) WHERE EntityType IS NOT NULL;
CREATE INDEX IX_EmailMessage_Status ON core.EmailMessage (Status) WHERE Status = 1;

CREATE TABLE core.EmailMessageAttachment (
    EmailMessageId int NOT NULL CONSTRAINT FK_EmailMessageAttachment_EmailMessage REFERENCES core.EmailMessage (Id),
    AttachmentId   int NOT NULL CONSTRAINT FK_EmailMessageAttachment_Attachment REFERENCES core.Attachment (Id),
    CONSTRAINT PK_EmailMessageAttachment PRIMARY KEY (EmailMessageId, AttachmentId)
);
