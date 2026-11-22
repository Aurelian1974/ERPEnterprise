-- Extrasele bancare (02.02, F2 3.1–3.4): profilele de format bancar, fisierele originale, extrasele, liniile (date de banca, imutabile)
-- si explicatia lor pe parti. Tipurile de operatiuni pot avea si sensul „Ambele” (3), folosit de viramentele interne si de sumele de
-- clarificat (F2 3.2).

ALTER TABLE fin.TreasuryOperationType DROP CONSTRAINT CK_TreasuryOperationType_Direction;
ALTER TABLE fin.TreasuryOperationType ADD CONSTRAINT CK_TreasuryOperationType_Direction CHECK (Direction IN (1, 2, 3));
GO

-- Profilul unei banci: cum se citeste extrasul si cum se scrie fisierul de plati (F2 3.1). StatementFormat: 1 CAMT.053, 2 MT940, 3 CSV,
-- 4 fara import. PaymentFormat: 1 pain.001.001.03, 2 pain.001.001.09, 3 CSV, 4 fara export.
CREATE TABLE fin.BankFormatProfile (
    Id                  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_BankFormatProfile PRIMARY KEY,
    Code                nvarchar(30)      NOT NULL,
    Name                nvarchar(200)     NOT NULL,
    BankBic             varchar(11)       NULL,
    StatementFormat     tinyint           NOT NULL,
    CsvMapping          nvarchar(max)     NULL,
    PaymentFormat       tinyint           NOT NULL,
    PaymentMapping      nvarchar(max)     NULL,
    MaxRemittanceLength smallint          NOT NULL CONSTRAINT DF_BankFormatProfile_MaxRemittanceLength DEFAULT (140),
    IsSystem            bit               NOT NULL CONSTRAINT DF_BankFormatProfile_IsSystem DEFAULT (0),
    IsActive            bit               NOT NULL CONSTRAINT DF_BankFormatProfile_IsActive DEFAULT (1),
    CreatedAt           datetime2(3)      NOT NULL CONSTRAINT DF_BankFormatProfile_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy           int               NOT NULL,
    ModifiedAt          datetime2(3)      NULL,
    ModifiedBy          int               NULL,
    RowVersion          rowversion        NOT NULL,
    CodeKey             AS UPPER(Code) PERSISTED,
    CONSTRAINT CK_BankFormatProfile_StatementFormat CHECK (StatementFormat BETWEEN 1 AND 4),
    CONSTRAINT CK_BankFormatProfile_PaymentFormat CHECK (PaymentFormat BETWEEN 1 AND 4),
    CONSTRAINT CK_BankFormatProfile_CsvMapping CHECK (CsvMapping IS NULL OR ISJSON(CsvMapping) = 1),
    CONSTRAINT CK_BankFormatProfile_PaymentMapping CHECK (PaymentMapping IS NULL OR ISJSON(PaymentMapping) = 1),
    CONSTRAINT CK_BankFormatProfile_MaxRemittanceLength CHECK (MaxRemittanceLength BETWEEN 35 AND 500)
);
GO

CREATE UNIQUE INDEX UX_BankFormatProfile_CodeKey ON fin.BankFormatProfile (CodeKey);
GO

ALTER TABLE fin.TreasuryAccount ADD
    BankFormatProfileId int           NULL CONSTRAINT FK_TreasuryAccount_BankFormatProfile REFERENCES fin.BankFormatProfile (Id),
    ImportFolder        nvarchar(260) NULL,
    LastStatementDate   date          NULL;
GO

-- Fisierul original al extrasului (document justificativ, pastrat 10 ani, Legea 82/1991), cu hash. Provizoriu in fin, pana la
-- atasamentele Nucleului (02.01 Attachment); atunci se muta, iar BankStatement primeste FileAttachmentId.
CREATE TABLE fin.BankStatementFile (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_BankStatementFile PRIMARY KEY,
    CompanyId         int               NOT NULL CONSTRAINT FK_BankStatementFile_Company REFERENCES core.Company (Id),
    TreasuryAccountId int               NOT NULL CONSTRAINT FK_BankStatementFile_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    FileName          nvarchar(260)     NOT NULL,
    FileSha256        char(64)          NOT NULL,
    SizeBytes         int               NOT NULL,
    Content           varbinary(max)    NOT NULL,
    CreatedAt         datetime2(3)      NOT NULL CONSTRAINT DF_BankStatementFile_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int               NOT NULL,
    CONSTRAINT CK_BankStatementFile_Sha256 CHECK (FileSha256 NOT LIKE '%[^0-9a-f]%')
);
GO

CREATE UNIQUE INDEX UX_BankStatementFile_TreasuryAccountId_FileSha256 ON fin.BankStatementFile (TreasuryAccountId, FileSha256);
GO

-- Extrasul bancar (F2 3.3). Source: 1 CAMT.053, 2 MT940, 3 CSV, 4 Manual, 5 Sold initial. Status: 1 Importat, 2 In reconciliere,
-- 3 Inchis. ContinuityStatus: 1 OK, 2 Gol fata de extrasul anterior (blocheaza validarea liniilor), 3 Gol acceptat.
CREATE TABLE fin.BankStatement (
    Id                  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_BankStatement PRIMARY KEY,
    CompanyId           int               NOT NULL CONSTRAINT FK_BankStatement_Company REFERENCES core.Company (Id),
    TreasuryAccountId   int               NOT NULL CONSTRAINT FK_BankStatement_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    StatementNo         nvarchar(35)      NULL,
    StatementDate       date              NOT NULL,
    PeriodFrom          date              NOT NULL,
    PeriodTo            date              NOT NULL,
    CurrencyCode        char(3)           NOT NULL CONSTRAINT FK_BankStatement_Currency REFERENCES core.Currency (Code),
    OpeningBalance      decimal(19,2)     NOT NULL,
    ClosingBalance      decimal(19,2)     NOT NULL,
    Source              tinyint           NOT NULL,
    BankStatementFileId int               NULL CONSTRAINT FK_BankStatement_BankStatementFile REFERENCES fin.BankStatementFile (Id),
    Status              tinyint           NOT NULL CONSTRAINT DF_BankStatement_Status DEFAULT (1),
    ContinuityStatus    tinyint           NOT NULL CONSTRAINT DF_BankStatement_ContinuityStatus DEFAULT (1),
    GapReason           nvarchar(500)     NULL,
    GapAcceptedAt       datetime2(3)      NULL,
    GapAcceptedBy       int               NULL,
    CreatedAt           datetime2(3)      NOT NULL CONSTRAINT DF_BankStatement_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy           int               NOT NULL,
    ModifiedAt          datetime2(3)      NULL,
    ModifiedBy          int               NULL,
    RowVersion          rowversion        NOT NULL,
    CONSTRAINT CK_BankStatement_Source CHECK (Source BETWEEN 1 AND 5),
    CONSTRAINT CK_BankStatement_Status CHECK (Status BETWEEN 1 AND 3),
    CONSTRAINT CK_BankStatement_ContinuityStatus CHECK (ContinuityStatus BETWEEN 1 AND 3),
    CONSTRAINT CK_BankStatement_Period CHECK (PeriodTo >= PeriodFrom)
);
GO

CREATE INDEX IX_BankStatement_TreasuryAccountId_StatementDate ON fin.BankStatement (TreasuryAccountId, StatementDate);
CREATE INDEX IX_BankStatement_CompanyId_Status ON fin.BankStatement (CompanyId, Status);
GO

-- Linia de extras (F2 3.4): datele bancii sunt imutabile; MatchStatus: 1 Nepotrivita, 2 Propusa, 3 Explicata, 4 Validata.
-- MatchSource: 1 Ordin de plata, 2 Factura, 3 Tranzit, 4 Card, 5 Regula, 6 Manual.
CREATE TABLE fin.BankStatementLine (
    Id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BankStatementLine PRIMARY KEY,
    BankStatementId     int                NOT NULL CONSTRAINT FK_BankStatementLine_BankStatement REFERENCES fin.BankStatement (Id),
    TreasuryAccountId   int                NOT NULL CONSTRAINT FK_BankStatementLine_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    LineNumber          int                NOT NULL,
    BookingDate         date               NOT NULL,
    ValueDate           date               NULL,
    Amount              decimal(19,2)      NOT NULL,
    CounterpartyName    nvarchar(200)      NULL,
    CounterpartyIban    varchar(34)        NULL,
    CounterpartyBic     varchar(11)        NULL,
    RemittanceInfo      nvarchar(500)      NULL,
    EndToEndId          varchar(35)        NULL,
    BankReference       varchar(100)       NOT NULL,
    BankTransactionCode varchar(35)        NULL,
    ForeignAmount       decimal(19,2)      NULL,
    ForeignCurrency     char(3)            NULL,
    MatchStatus         tinyint            NOT NULL CONSTRAINT DF_BankStatementLine_MatchStatus DEFAULT (1),
    MatchSource         tinyint            NULL,
    PartnerId           int                NULL CONSTRAINT FK_BankStatementLine_Partner REFERENCES core.Partner (Id),
    ExtractedFiscalCode varchar(20)        NULL,
    ExtractedDocumentNo nvarchar(50)       NULL,
    ValidatedAt         datetime2(3)       NULL,
    ValidatedBy         int                NULL,
    ReversedAt          datetime2(3)       NULL,
    CreatedAt           datetime2(3)       NOT NULL CONSTRAINT DF_BankStatementLine_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy           int                NOT NULL,
    ModifiedAt          datetime2(3)       NULL,
    ModifiedBy          int                NULL,
    RowVersion          rowversion         NOT NULL,
    RemittanceSearch    AS (RemittanceInfo COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED,
    CONSTRAINT CK_BankStatementLine_MatchStatus CHECK (MatchStatus IN (1, 2, 3, 4)),
    CONSTRAINT CK_BankStatementLine_MatchSource CHECK (MatchSource IS NULL OR MatchSource BETWEEN 1 AND 6),
    CONSTRAINT CK_BankStatementLine_AmountNotZero CHECK (Amount <> 0),
    CONSTRAINT CK_BankStatementLine_ValidatedHasUser CHECK (MatchStatus <> 4 OR ValidatedBy IS NOT NULL)
);
GO

CREATE UNIQUE INDEX UX_BankStatementLine_TreasuryAccountId_BankReference ON fin.BankStatementLine (TreasuryAccountId, BankReference);
CREATE INDEX IX_BankStatementLine_TreasuryAccountId_MatchStatus_BookingDate ON fin.BankStatementLine (TreasuryAccountId, MatchStatus, BookingDate)
    INCLUDE (Amount, CounterpartyIban, PartnerId);
CREATE INDEX IX_BankStatementLine_EndToEndId ON fin.BankStatementLine (EndToEndId) WHERE EndToEndId IS NOT NULL;
CREATE INDEX IX_BankStatementLine_BankStatementId ON fin.BankStatementLine (BankStatementId);
CREATE INDEX IX_BankStatementLine_PartnerId ON fin.BankStatementLine (PartnerId) WHERE PartnerId IS NOT NULL;
CREATE INDEX IX_BankStatementLine_CounterpartyIban ON fin.BankStatementLine (CounterpartyIban) WHERE CounterpartyIban IS NOT NULL;
GO

-- Partea unei linii (F2 3.4): suma are semnul liniei; suma partilor = suma liniei. Legaturile spre OP si decontarile de card primesc
-- FK odata cu tabelele lor.
CREATE TABLE fin.BankStatementLinePart (
    Id                       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BankStatementLinePart PRIMARY KEY,
    BankStatementLineId      bigint            NOT NULL,
    PartNumber               smallint          NOT NULL,
    TreasuryOperationTypeId  int               NOT NULL CONSTRAINT FK_BankStatementLinePart_TreasuryOperationType REFERENCES fin.TreasuryOperationType (Id),
    Amount                   decimal(19,2)     NOT NULL,
    PartnerId                int               NULL CONSTRAINT FK_BankStatementLinePart_Partner REFERENCES core.Partner (Id),
    EmployeeId               int               NULL CONSTRAINT FK_BankStatementLinePart_Employee REFERENCES core.Employee (Id),
    SettlementRef            nvarchar(100)     NULL,
    PaymentOrderId           int               NULL,
    CashDocumentId           int               NULL CONSTRAINT FK_BankStatementLinePart_CashDocument REFERENCES fin.CashDocument (Id),
    CounterTreasuryAccountId int               NULL CONSTRAINT FK_BankStatementLinePart_CounterTreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    CardSettlementExpectedId int               NULL,
    BudgetObligationRef      nvarchar(50)      NULL,
    CostCenterId             int               NULL CONSTRAINT FK_BankStatementLinePart_CostCenter REFERENCES core.DimensionValue (Id),
    ProjectId                int               NULL CONSTRAINT FK_BankStatementLinePart_Project REFERENCES core.DimensionValue (Id),
    Note                     nvarchar(500)     NULL,
    CONSTRAINT FK_BankStatementLinePart_BankStatementLine FOREIGN KEY (BankStatementLineId) REFERENCES fin.BankStatementLine (Id) ON DELETE CASCADE,
    CONSTRAINT UQ_BankStatementLinePart_Line_PartNumber UNIQUE (BankStatementLineId, PartNumber),
    CONSTRAINT CK_BankStatementLinePart_AmountNotZero CHECK (Amount <> 0)
);
GO

CREATE INDEX IX_BankStatementLinePart_PaymentOrderId ON fin.BankStatementLinePart (PaymentOrderId) WHERE PaymentOrderId IS NOT NULL;
CREATE INDEX IX_BankStatementLinePart_CashDocumentId ON fin.BankStatementLinePart (CashDocumentId) WHERE CashDocumentId IS NOT NULL;
GO

-- Liniile unui extras la import (fin.usp_BankStatement_Import, SP-07).
CREATE TYPE fin.BankStatementLineList AS TABLE (
    LineNumber          int           NOT NULL PRIMARY KEY,
    BookingDate         date          NOT NULL,
    ValueDate           date          NULL,
    Amount              decimal(19,2) NOT NULL,
    CounterpartyName    nvarchar(200) NULL,
    CounterpartyIban    varchar(34)   NULL,
    CounterpartyBic     varchar(11)   NULL,
    RemittanceInfo      nvarchar(500) NULL,
    EndToEndId          varchar(35)   NULL,
    BankReference       varchar(100)  NOT NULL,
    BankTransactionCode varchar(35)   NULL,
    ForeignAmount       decimal(19,2) NULL,
    ForeignCurrency     char(3)       NULL,
    ExtractedFiscalCode varchar(20)   NULL,
    ExtractedDocumentNo nvarchar(50)  NULL
);
GO
