-- Compensarile (02.02, F3 3.4, 4.4): procesul-verbal si liniile lui. Kind: 1 Bilaterala la acelasi partener (client <-> furnizor),
-- 2 Bilaterala intre doua roluri cu cesiune, 3 Cu trei parti, 4 Din sesiune de compensare externa. Status: 1 Ciorna, 2 Validat, 3 Stornat.
-- Linia: partida si suma care se stinge, cu semnul partidei (debit +, credit −); suma liniilor = 0.
CREATE TABLE fin.Compensation (
    Id                  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Compensation PRIMARY KEY,
    CompanyId           int               NOT NULL CONSTRAINT FK_Compensation_Company REFERENCES core.Company (Id),
    Number              nvarchar(40)      NULL,
    DocumentDate        date              NOT NULL,
    Kind                tinyint           NOT NULL,
    ExternalRef         nvarchar(100)     NULL,
    CurrencyCode        char(3)           NOT NULL CONSTRAINT FK_Compensation_Currency REFERENCES core.Currency (Code),
    TotalAmount         decimal(19,2)     NOT NULL CONSTRAINT DF_Compensation_TotalAmount DEFAULT (0),
    Status              tinyint           NOT NULL CONSTRAINT DF_Compensation_Status DEFAULT (1),
    SignedAttachmentId  int               NULL CONSTRAINT FK_Compensation_Attachment REFERENCES core.Attachment (Id),
    Note                nvarchar(500)     NULL,
    ValidatedAt         datetime2(3)      NULL,
    ValidatedBy         int               NULL,
    ReversedAt          datetime2(3)      NULL,
    ReversedBy          int               NULL,
    CreatedAt           datetime2(3)      NOT NULL CONSTRAINT DF_Compensation_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy           int               NOT NULL,
    ModifiedAt          datetime2(3)      NULL,
    ModifiedBy          int               NULL,
    RowVersion          rowversion        NOT NULL,
    CONSTRAINT CK_Compensation_Kind CHECK (Kind BETWEEN 1 AND 4),
    CONSTRAINT CK_Compensation_Status CHECK (Status IN (1, 2, 3)),
    CONSTRAINT CK_Compensation_Number CHECK (Status = 1 OR Number IS NOT NULL)
);
CREATE UNIQUE INDEX UX_Compensation_CompanyId_Number ON fin.Compensation (CompanyId, Number) WHERE Number IS NOT NULL;
CREATE INDEX IX_Compensation_CompanyId_DocumentDate ON fin.Compensation (CompanyId, DocumentDate);

CREATE TABLE fin.CompensationLine (
    Id              bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompensationLine PRIMARY KEY,
    CompensationId  int               NOT NULL CONSTRAINT FK_CompensationLine_Compensation REFERENCES fin.Compensation (Id),
    LineNumber      smallint          NOT NULL,
    OpenItemId      bigint            NOT NULL CONSTRAINT FK_CompensationLine_OpenItem REFERENCES fin.OpenItem (Id),
    Amount          decimal(19,2)     NOT NULL,
    CONSTRAINT CK_CompensationLine_Amount CHECK (Amount <> 0),
    CONSTRAINT UQ_CompensationLine_Compensation_OpenItem UNIQUE (CompensationId, OpenItemId)
);
CREATE INDEX IX_CompensationLine_OpenItemId ON fin.CompensationLine (OpenItemId);

ALTER TABLE fin.Allocation ADD CONSTRAINT FK_Allocation_Compensation FOREIGN KEY (CompensationId) REFERENCES fin.Compensation (Id);

CREATE TYPE fin.CompensationLineList AS TABLE (
    LineNumber  smallint       NOT NULL PRIMARY KEY,
    OpenItemId  bigint         NOT NULL,
    Amount      decimal(19,2)  NOT NULL
);
