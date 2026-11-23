-- Confirmarile de sold (02.02, F3 3.9, 4.6; macheta f3-07): loturile (data soldului, rolul, pragul) si confirmarile pe partener si rol, cu
-- PDF-ul generat (partidele deschise la data), trimiterea pe email si raspunsul. ResponseStatus: 0 Generata, 1 Trimisa, 2 Confirmata,
-- 3 Contestata, 4 Fara raspuns. Soldurile sunt in lei, cu semnul conventiei (debit +, credit −).
CREATE TABLE fin.BalanceConfirmationBatch (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_BalanceConfirmationBatch PRIMARY KEY,
    CompanyId   int               NOT NULL CONSTRAINT FK_BalanceConfirmationBatch_Company REFERENCES core.Company (Id),
    Number      nvarchar(40)      NOT NULL,
    AsOfDate    date              NOT NULL,
    Role        tinyint           NULL,
    MinBalance  decimal(19,2)     NOT NULL,
    SentAt      datetime2(3)      NULL,
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_BalanceConfirmationBatch_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    CONSTRAINT UQ_BalanceConfirmationBatch_Number UNIQUE (CompanyId, Number),
    CONSTRAINT CK_BalanceConfirmationBatch_Role CHECK (Role IS NULL OR Role IN (1, 2))
);

CREATE TABLE fin.BalanceConfirmation (
    Id                     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_BalanceConfirmation PRIMARY KEY,
    CompanyId              int               NOT NULL CONSTRAINT FK_BalanceConfirmation_Company REFERENCES core.Company (Id),
    BatchId                int               NOT NULL CONSTRAINT FK_BalanceConfirmation_Batch REFERENCES fin.BalanceConfirmationBatch (Id),
    PartnerId              int               NOT NULL CONSTRAINT FK_BalanceConfirmation_Partner REFERENCES core.Partner (Id),
    Role                   tinyint           NOT NULL,
    AsOfDate               date              NOT NULL,
    Number                 nvarchar(50)      NOT NULL,
    OurBalance             decimal(19,2)     NOT NULL,
    GeneratedAttachmentId  int               NULL CONSTRAINT FK_BalanceConfirmation_Attachment_Generated REFERENCES core.Attachment (Id),
    SentAt                 datetime2(3)      NULL,
    SentTo                 nvarchar(254)     NULL,
    EmailMessageId         int               NULL CONSTRAINT FK_BalanceConfirmation_EmailMessage REFERENCES core.EmailMessage (Id),
    ResponseStatus         tinyint           NOT NULL CONSTRAINT DF_BalanceConfirmation_ResponseStatus DEFAULT (0),
    ResponseDate           date              NULL,
    PartnerBalance         decimal(19,2)     NULL,
    Difference             decimal(19,2)     NULL,
    ResponseNote           nvarchar(1000)    NULL,
    ResponseAttachmentId   int               NULL CONSTRAINT FK_BalanceConfirmation_Attachment_Response REFERENCES core.Attachment (Id),
    IsResolved             bit               NOT NULL CONSTRAINT DF_BalanceConfirmation_IsResolved DEFAULT (0),
    ModifiedAt             datetime2(3)      NULL,
    ModifiedBy             int               NULL,
    RowVersion             rowversion        NOT NULL,
    CONSTRAINT UQ_BalanceConfirmation_Number UNIQUE (CompanyId, Number),
    CONSTRAINT CK_BalanceConfirmation_Role CHECK (Role IN (1, 2)),
    CONSTRAINT CK_BalanceConfirmation_ResponseStatus CHECK (ResponseStatus BETWEEN 0 AND 4)
);
CREATE INDEX IX_BalanceConfirmation_CompanyId_AsOfDate_ResponseStatus ON fin.BalanceConfirmation (CompanyId, AsOfDate, ResponseStatus);
CREATE INDEX IX_BalanceConfirmation_BatchId ON fin.BalanceConfirmation (BatchId);
