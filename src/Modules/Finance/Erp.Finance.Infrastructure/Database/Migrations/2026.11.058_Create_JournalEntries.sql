-- 02.02 Contabilitate, C2.1: jurnalele, notele contabile si liniile lor (C2 3.5, 3.6). Jurnalele livrate sunt comune instalarii
-- (CompanyId NULL, seed 10_Journals); numarul notei e pe firma, jurnal si an, din fin.JournalSequence, alocat la validare fara goluri.
-- Linia repeta CompanyId si perioada (abaterea de la SQL-05 scrisa in C2 7) si nu are coloane de modificare (liniile validate nu se schimba).

CREATE TABLE fin.Journal (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Journal PRIMARY KEY,
    CompanyId   int               NULL CONSTRAINT FK_Journal_Company REFERENCES core.Company (Id),
    Code        nvarchar(10)      NOT NULL,
    Name        nvarchar(100)     NOT NULL,
    IsSystem    bit               NOT NULL CONSTRAINT DF_Journal_IsSystem DEFAULT (0),
    IsActive    bit               NOT NULL CONSTRAINT DF_Journal_IsActive DEFAULT (1),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_Journal_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    RowVersion  rowversion        NOT NULL,
    CodeKey     AS UPPER(Code) PERSISTED
);
CREATE UNIQUE INDEX UX_Journal_Company_CodeKey ON fin.Journal (CompanyId, CodeKey);

CREATE TABLE fin.JournalSequence (
    CompanyId   int       NOT NULL CONSTRAINT FK_JournalSequence_Company REFERENCES core.Company (Id),
    JournalId   int       NOT NULL CONSTRAINT FK_JournalSequence_Journal REFERENCES fin.Journal (Id),
    [Year]      smallint  NOT NULL,
    LastNumber  int       NOT NULL,
    CONSTRAINT PK_JournalSequence PRIMARY KEY (CompanyId, JournalId, [Year])
);
CREATE INDEX IX_JournalSequence_JournalId ON fin.JournalSequence (JournalId);

CREATE TABLE fin.JournalEntry (
    Id                    bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_JournalEntry PRIMARY KEY,
    CompanyId             int               NOT NULL CONSTRAINT FK_JournalEntry_Company REFERENCES core.Company (Id),
    JournalId             int               NOT NULL CONSTRAINT FK_JournalEntry_Journal REFERENCES fin.Journal (Id),
    Number                int               NULL,
    EntryDate             date              NOT NULL,
    AccountingDate        date              NOT NULL,
    PeriodYear            smallint          NOT NULL,
    PeriodMonth           tinyint           NOT NULL,
    SourceKind            tinyint           NOT NULL,
    SourceModule          varchar(20)       NULL,
    SourceDocumentType    varchar(50)       NULL,
    SourceDocumentId      bigint            NULL,
    SourceDocumentNumber  nvarchar(50)      NULL,
    SourceEventId         uniqueidentifier  NULL,
    PostingModelVariantId int               NULL,
    Status                tinyint           NOT NULL CONSTRAINT DF_JournalEntry_Status DEFAULT (1),
    ReversalOfId          bigint            NULL CONSTRAINT FK_JournalEntry_ReversalOf REFERENCES fin.JournalEntry (Id),
    Description           nvarchar(400)     NULL,
    TotalDebit            decimal(19,2)     NOT NULL CONSTRAINT DF_JournalEntry_TotalDebit DEFAULT (0),
    TotalCredit           decimal(19,2)     NOT NULL CONSTRAINT DF_JournalEntry_TotalCredit DEFAULT (0),
    ValidatedAt           datetime2(3)      NULL,
    ValidatedBy           int               NULL,
    ApprovedBy            int               NULL,
    CreatedAt             datetime2(3)      NOT NULL CONSTRAINT DF_JournalEntry_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy             int               NOT NULL,
    ModifiedAt            datetime2(3)      NULL,
    ModifiedBy            int               NULL,
    RowVersion            rowversion        NOT NULL,
    -- SourceKind: 1 eveniment, 2 manuala, 3 reclasificare, 4 inchidere, 5 deschidere, 6 storno. Status: 1 ciorna, 2 validata, 3 stornata.
    CONSTRAINT CK_JournalEntry_SourceKind CHECK (SourceKind BETWEEN 1 AND 6),
    CONSTRAINT CK_JournalEntry_Status CHECK (Status BETWEEN 1 AND 3),
    CONSTRAINT CK_JournalEntry_Period CHECK (PeriodYear = YEAR(AccountingDate) AND PeriodMonth = MONTH(AccountingDate)),
    -- Regula 2 (C2-A7): o nota validata e echilibrata pe lei si are numar.
    CONSTRAINT CK_JournalEntry_Validated CHECK (Status = 1 OR (TotalDebit = TotalCredit AND Number IS NOT NULL AND ValidatedAt IS NOT NULL))
);
CREATE UNIQUE INDEX UX_JournalEntry_SourceEventId ON fin.JournalEntry (SourceEventId) WHERE SourceEventId IS NOT NULL;
CREATE UNIQUE INDEX UX_JournalEntry_Number ON fin.JournalEntry (CompanyId, JournalId, PeriodYear, Number) WHERE Number IS NOT NULL;
CREATE UNIQUE INDEX UX_JournalEntry_ReversalOfId ON fin.JournalEntry (ReversalOfId) WHERE ReversalOfId IS NOT NULL;
CREATE INDEX IX_JournalEntry_Company_AccountingDate ON fin.JournalEntry (CompanyId, AccountingDate) INCLUDE (JournalId, Number, Status, SourceKind);
CREATE INDEX IX_JournalEntry_JournalId ON fin.JournalEntry (JournalId);
CREATE INDEX IX_JournalEntry_Source ON fin.JournalEntry (SourceDocumentType, SourceDocumentId) WHERE SourceDocumentId IS NOT NULL;

CREATE TABLE fin.JournalLine (
    Id               bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_JournalLine PRIMARY KEY,
    EntryId          bigint             NOT NULL CONSTRAINT FK_JournalLine_JournalEntry REFERENCES fin.JournalEntry (Id) ON DELETE CASCADE,
    CompanyId        int                NOT NULL,
    PeriodYear       smallint           NOT NULL,
    PeriodMonth      tinyint            NOT NULL,
    LineNumber           int                NOT NULL,
    AccountId        int                NOT NULL CONSTRAINT FK_JournalLine_GlAccount REFERENCES fin.GlAccount (Id),
    Side             tinyint            NOT NULL,
    AmountRon        decimal(19,2)      NOT NULL,
    DebitRon         AS (CASE WHEN Side = 1 THEN AmountRon ELSE CAST(0 AS decimal(19,2)) END) PERSISTED,
    CreditRon        AS (CASE WHEN Side = 2 THEN AmountRon ELSE CAST(0 AS decimal(19,2)) END) PERSISTED,
    CurrencyCode     char(3)            NULL CONSTRAINT FK_JournalLine_Currency REFERENCES core.Currency (Code),
    AmountCurrency   decimal(19,2)      NULL,
    PartnerId        int                NULL CONSTRAINT FK_JournalLine_Partner REFERENCES core.Partner (Id),
    EmployeeId       int                NULL CONSTRAINT FK_JournalLine_Employee REFERENCES core.Employee (Id),
    CostCenterId     int                NULL CONSTRAINT FK_JournalLine_CostCenter REFERENCES core.DimensionValue (Id),
    ProjectId        int                NULL CONSTRAINT FK_JournalLine_Project REFERENCES core.DimensionValue (Id),
    WarehouseId      int                NULL CONSTRAINT FK_JournalLine_Warehouse REFERENCES core.DimensionValue (Id),
    SiteId           int                NULL CONSTRAINT FK_JournalLine_Site REFERENCES core.DimensionValue (Id),
    ItemId           int                NULL CONSTRAINT FK_JournalLine_Item REFERENCES core.Item (Id),
    VatRateCode      nvarchar(10)       NULL,
    VatCategory      nvarchar(4)        NULL,
    Description      nvarchar(400)      NULL,
    ResolvedFrom     tinyint            NULL,
    SourceLineRef    nvarchar(40)       NULL,
    CreatedAt        datetime2(3)       NOT NULL CONSTRAINT DF_JournalLine_CreatedAt DEFAULT (sysutcdatetime()),
    CONSTRAINT UQ_JournalLine_Entry_LineNo UNIQUE (EntryId, LineNumber),
    CONSTRAINT CK_JournalLine_Side CHECK (Side IN (1, 2)),
    CONSTRAINT CK_JournalLine_Amount CHECK (AmountRon <> 0),
    CONSTRAINT CK_JournalLine_Currency CHECK ((CurrencyCode IS NULL AND AmountCurrency IS NULL) OR (CurrencyCode IS NOT NULL AND AmountCurrency IS NOT NULL))
);
CREATE INDEX IX_JournalLine_AccountId_EntryId ON fin.JournalLine (AccountId, EntryId);
CREATE INDEX IX_JournalLine_Company_Period_Account ON fin.JournalLine (CompanyId, PeriodYear, PeriodMonth, AccountId) INCLUDE (DebitRon, CreditRon, PartnerId);
CREATE INDEX IX_JournalLine_PartnerId ON fin.JournalLine (PartnerId) WHERE PartnerId IS NOT NULL;
CREATE INDEX IX_JournalLine_EmployeeId ON fin.JournalLine (EmployeeId) WHERE EmployeeId IS NOT NULL;
CREATE INDEX IX_JournalLine_CostCenterId ON fin.JournalLine (CostCenterId) WHERE CostCenterId IS NOT NULL;
CREATE INDEX IX_JournalLine_ProjectId ON fin.JournalLine (ProjectId) WHERE ProjectId IS NOT NULL;
CREATE INDEX IX_JournalLine_WarehouseId ON fin.JournalLine (WarehouseId) WHERE WarehouseId IS NOT NULL;
CREATE INDEX IX_JournalLine_SiteId ON fin.JournalLine (SiteId) WHERE SiteId IS NOT NULL;
CREATE INDEX IX_JournalLine_ItemId ON fin.JournalLine (ItemId) WHERE ItemId IS NOT NULL;
CREATE INDEX IX_JournalLine_CurrencyCode ON fin.JournalLine (CurrencyCode) WHERE CurrencyCode IS NOT NULL;
