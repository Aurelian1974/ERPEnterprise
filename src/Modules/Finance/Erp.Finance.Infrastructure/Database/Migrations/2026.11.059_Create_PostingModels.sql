-- 02.02 Contabilitate, C2.2: modelele de contare (C2 3.2), cheile de cont (3.3) si coada „De contat” (3.7). Un model pe tip de eveniment, cu
-- variante pe flaguri, tipul partenerului si tipul documentului, in vigoare pe date; liniile spun componenta, partea, semnul, cheia de cont si
-- contul implicit. Modelele livrate vin din seed-ul 11_PostingModels. Coada tine evenimentele care nu au putut fi contate, cu motivul.

CREATE TABLE fin.AccountKey (
    Code         varchar(40)    NOT NULL CONSTRAINT PK_AccountKey PRIMARY KEY,
    Name         nvarchar(200)  NOT NULL,
    Chain        nvarchar(400)  NOT NULL
);

CREATE TABLE fin.PostingModel (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PostingModel PRIMARY KEY,
    EventType   varchar(80)       NOT NULL,
    Name        nvarchar(200)     NOT NULL,
    JournalId   int               NOT NULL CONSTRAINT FK_PostingModel_Journal REFERENCES fin.Journal (Id),
    IsSystem    bit               NOT NULL CONSTRAINT DF_PostingModel_IsSystem DEFAULT (0),
    IsActive    bit               NOT NULL CONSTRAINT DF_PostingModel_IsActive DEFAULT (1),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_PostingModel_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    RowVersion  rowversion        NOT NULL
);
CREATE UNIQUE INDEX UX_PostingModel_EventType ON fin.PostingModel (EventType);
CREATE INDEX IX_PostingModel_JournalId ON fin.PostingModel (JournalId);

CREATE TABLE fin.PostingModelVariant (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PostingModelVariant PRIMARY KEY,
    ModelId         int               NOT NULL CONSTRAINT FK_PostingModelVariant_Model REFERENCES fin.PostingModel (Id),
    Priority        int               NOT NULL,
    Name            nvarchar(200)     NOT NULL,
    RequiredFlags   nvarchar(200)     NULL,
    ForbiddenFlags  nvarchar(200)     NULL,
    PartnerKind     tinyint           NULL,
    DocumentType    varchar(50)       NULL,
    ValidFrom       date              NOT NULL,
    ValidTo         date              NULL,
    IsActive        bit               NOT NULL CONSTRAINT DF_PostingModelVariant_IsActive DEFAULT (1),
    CreatedAt       datetime2(3)      NOT NULL CONSTRAINT DF_PostingModelVariant_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy       int               NOT NULL,
    ModifiedAt      datetime2(3)      NULL,
    ModifiedBy      int               NULL,
    RowVersion      rowversion        NOT NULL,
    CONSTRAINT UQ_PostingModelVariant_Model_Priority_ValidFrom UNIQUE (ModelId, Priority, ValidFrom),
    CONSTRAINT CK_PostingModelVariant_PartnerKind CHECK (PartnerKind IS NULL OR PartnerKind BETWEEN 1 AND 4),
    CONSTRAINT CK_PostingModelVariant_Validity CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
CREATE INDEX IX_PostingModelVariant_Model_Active_ValidFrom ON fin.PostingModelVariant (ModelId, IsActive, ValidFrom);

CREATE TABLE fin.PostingModelLine (
    Id                       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PostingModelLine PRIMARY KEY,
    VariantId                int               NOT NULL CONSTRAINT FK_PostingModelLine_Variant REFERENCES fin.PostingModelVariant (Id) ON DELETE CASCADE,
    LineNumber               int               NOT NULL,
    Component                varchar(40)       NOT NULL,
    FilterVatCategory        nvarchar(4)       NULL,
    FilterOperationTypeCode  nvarchar(30)      NULL,
    Side                     tinyint           NOT NULL,
    Sign                     smallint          NOT NULL CONSTRAINT DF_PostingModelLine_Sign DEFAULT (1),
    AccountKey               varchar(40)       NOT NULL CONSTRAINT FK_PostingModelLine_AccountKey REFERENCES fin.AccountKey (Code),
    DefaultAccountId         int               NULL CONSTRAINT FK_PostingModelLine_DefaultAccount REFERENCES fin.GlAccount (Id),
    CopyDimensions           tinyint           NOT NULL CONSTRAINT DF_PostingModelLine_CopyDimensions DEFAULT (63),
    GroupBy                  tinyint           NOT NULL CONSTRAINT DF_PostingModelLine_GroupBy DEFAULT (1),
    DescriptionTemplate      nvarchar(200)     NULL,
    CONSTRAINT UQ_PostingModelLine_Variant_LineNumber UNIQUE (VariantId, LineNumber),
    CONSTRAINT CK_PostingModelLine_Side CHECK (Side IN (1, 2)),
    CONSTRAINT CK_PostingModelLine_Sign CHECK (Sign IN (1, -1)),
    CONSTRAINT CK_PostingModelLine_GroupBy CHECK (GroupBy BETWEEN 0 AND 2)
);
CREATE INDEX IX_PostingModelLine_AccountKey ON fin.PostingModelLine (AccountKey);
CREATE INDEX IX_PostingModelLine_DefaultAccountId ON fin.PostingModelLine (DefaultAccountId) WHERE DefaultAccountId IS NOT NULL;

CREATE TABLE fin.PostingQueue (
    Id                    bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PostingQueue PRIMARY KEY,
    EventId               uniqueidentifier  NOT NULL,
    EventType             varchar(80)       NOT NULL,
    CompanyId             int               NOT NULL CONSTRAINT FK_PostingQueue_Company REFERENCES core.Company (Id),
    SourceDocumentType    varchar(50)       NULL,
    SourceDocumentId      bigint            NULL,
    SourceDocumentNumber  nvarchar(50)      NULL,
    AccountingDate        date              NOT NULL,
    Reason                tinyint           NOT NULL,
    Details               nvarchar(2000)    NULL,
    Payload               nvarchar(max)     NOT NULL,
    Status                tinyint           NOT NULL CONSTRAINT DF_PostingQueue_Status DEFAULT (1),
    Attempts              int               NOT NULL CONSTRAINT DF_PostingQueue_Attempts DEFAULT (1),
    LastAttemptAt         datetime2(3)      NOT NULL CONSTRAINT DF_PostingQueue_LastAttemptAt DEFAULT (sysutcdatetime()),
    ResolvedEntryId       bigint            NULL CONSTRAINT FK_PostingQueue_Entry REFERENCES fin.JournalEntry (Id),
    IgnoreReason          nvarchar(500)     NULL,
    CreatedAt             datetime2(3)      NOT NULL CONSTRAINT DF_PostingQueue_CreatedAt DEFAULT (sysutcdatetime()),
    ModifiedAt            datetime2(3)      NULL,
    ModifiedBy            int               NULL,
    RowVersion            rowversion        NOT NULL,
    -- Reason: 1 cont negasit, 2 cont invalid, 3 dimensiune lipsa, 4 perioada inchisa, 5 model lipsa, 6 dezechilibru, 7 eroare neasteptata.
    -- Status: 1 in asteptare, 2 rezolvat, 3 ignorat. Payload: contractul de contare (JSON), ca reincercarea sa nu mai ceara evenimentul.
    CONSTRAINT UQ_PostingQueue_EventId UNIQUE (EventId),
    CONSTRAINT CK_PostingQueue_Reason CHECK (Reason BETWEEN 1 AND 7),
    CONSTRAINT CK_PostingQueue_Status CHECK (Status BETWEEN 1 AND 3)
);
CREATE INDEX IX_PostingQueue_Company_Status_Reason ON fin.PostingQueue (CompanyId, Status, Reason) INCLUDE (AccountingDate);
CREATE INDEX IX_PostingQueue_ResolvedEntryId ON fin.PostingQueue (ResolvedEntryId) WHERE ResolvedEntryId IS NOT NULL;
