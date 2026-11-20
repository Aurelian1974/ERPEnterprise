-- Importurile din Excel (02.01, 4.8; macheta 07g): jobul retine raportul de validare si randurile valide normalizate, ca
-- confirmarea sa nu ceara reincarcarea fisierului. Status: 1 validat (asteapta confirmarea), 2 importat, 3 anulat,
-- 4 importat partial (un lot a esuat; reluarea importului nu dubleaza, randurile existente se actualizeaza).
CREATE TABLE core.ImportJob (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ImportJob PRIMARY KEY,
    EntityType   varchar(20)       NOT NULL,
    CompanyId    int               NULL CONSTRAINT FK_ImportJob_Company REFERENCES core.Company (Id),
    FileName     nvarchar(260)     NOT NULL,
    Status       tinyint           NOT NULL,
    AnafCheck    bit               NOT NULL,
    TotalRows    int               NOT NULL,
    ValidRows    int               NOT NULL,
    ErrorRows    int               NOT NULL,
    WarningRows  int               NOT NULL,
    ImportedRows int               NOT NULL CONSTRAINT DF_ImportJob_ImportedRows DEFAULT (0),
    ErrorReport  nvarchar(max)     NULL,      -- JSON: erorile si avertizarile pe rand si coloana, cu valorile randurilor gresite
    Payload      nvarchar(max)     NULL,      -- JSON: randurile valide normalizate; se goleste dupa import
    LastError    nvarchar(1000)    NULL,
    CreatedAt    datetime2(3)      NOT NULL CONSTRAINT DF_ImportJob_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy    int               NOT NULL,
    CompletedAt  datetime2(3)      NULL,
    ModifiedAt   datetime2(3)      NULL,
    ModifiedBy   int               NULL,
    RowVersion   rowversion        NOT NULL,
    CONSTRAINT CK_ImportJob_EntityType CHECK (EntityType IN ('Partner', 'Item')),
    CONSTRAINT CK_ImportJob_Status CHECK (Status BETWEEN 1 AND 4),
    CONSTRAINT CK_ImportJob_ErrorReportJson CHECK (ErrorReport IS NULL OR ISJSON(ErrorReport) = 1),
    CONSTRAINT CK_ImportJob_PayloadJson CHECK (Payload IS NULL OR ISJSON(Payload) = 1)
);
CREATE INDEX IX_ImportJob_EntityType ON core.ImportJob (EntityType, CreatedAt);
GO

-- Optiunile unei proprietati de tip lista, trimise la salvarea definitiei (Configurare › Proprietati, macheta 07l).
CREATE TYPE core.PropertyOptionList AS TABLE (
    Id        int           NULL,
    Code      nvarchar(30)  NOT NULL,
    Label     nvarchar(100) NOT NULL,
    SortOrder int           NOT NULL,
    IsActive  bit           NOT NULL
);
GO

-- Cautarea partenerilor existenti dupa cod si CUI, la importul in loturi.
CREATE TYPE core.PartnerKeyList AS TABLE (
    RowNumber  int          NOT NULL,
    Code       nvarchar(30) NULL,
    FiscalCode nvarchar(30) NULL
);
GO
