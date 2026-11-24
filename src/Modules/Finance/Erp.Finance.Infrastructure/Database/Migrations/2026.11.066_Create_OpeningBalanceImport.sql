-- 02.02 Contabilitate, C3.4: soldurile initiale la punerea in functiune (C3 3.7, decizia 7): balanta de la sfarsitul lunii dinaintea startului,
-- pe cont, partener si moneda, cu soldul initial al anului, rulajele cumulate si soldul final. La validare devine doua note de deschidere
-- (soldurile initiale in prima zi a anului, rulajele in ultima zi a lunii dinaintea startului), iar lunile de dinainte se inchid ca importate.
-- Status: 1 ciorna, 2 validat, 3 anulat.
CREATE TABLE fin.OpeningBalanceImport (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OpeningBalanceImport PRIMARY KEY,
    CompanyId        int               NOT NULL CONSTRAINT FK_OpeningBalanceImport_Company REFERENCES core.Company (Id),
    StartDate        date              NOT NULL,
    Status           tinyint           NOT NULL CONSTRAINT DF_OpeningBalanceImport_Status DEFAULT (1),
    FileName         nvarchar(260)     NULL,
    OpeningEntryId   bigint            NULL CONSTRAINT FK_OpeningBalanceImport_OpeningEntry REFERENCES fin.JournalEntry (Id),
    TurnoverEntryId  bigint            NULL CONSTRAINT FK_OpeningBalanceImport_TurnoverEntry REFERENCES fin.JournalEntry (Id),
    ValidatedAt      datetime2(3)      NULL,
    ValidatedBy      int               NULL,
    CreatedAt        datetime2(3)      NOT NULL CONSTRAINT DF_OpeningBalanceImport_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int               NOT NULL,
    ModifiedAt       datetime2(3)      NULL,
    ModifiedBy       int               NULL,
    RowVersion       rowversion        NOT NULL,
    CONSTRAINT CK_OpeningBalanceImport_Status CHECK (Status BETWEEN 1 AND 3),
    CONSTRAINT CK_OpeningBalanceImport_StartDate CHECK (DAY(StartDate) = 1)
);
CREATE INDEX IX_OpeningBalanceImport_CompanyId ON fin.OpeningBalanceImport (CompanyId, Status);
CREATE INDEX IX_OpeningBalanceImport_OpeningEntryId ON fin.OpeningBalanceImport (OpeningEntryId) WHERE OpeningEntryId IS NOT NULL;
CREATE INDEX IX_OpeningBalanceImport_TurnoverEntryId ON fin.OpeningBalanceImport (TurnoverEntryId) WHERE TurnoverEntryId IS NOT NULL;

CREATE TABLE fin.OpeningBalanceImportRow (
    Id              bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_OpeningBalanceImportRow PRIMARY KEY,
    ImportId        int               NOT NULL CONSTRAINT FK_OpeningBalanceImportRow_Import REFERENCES fin.OpeningBalanceImport (Id) ON DELETE CASCADE,
    RowNumber       int               NOT NULL,
    AccountId       int               NOT NULL CONSTRAINT FK_OpeningBalanceImportRow_Account REFERENCES fin.GlAccount (Id),
    PartnerId       int               NULL CONSTRAINT FK_OpeningBalanceImportRow_Partner REFERENCES core.Partner (Id),
    CurrencyCode    char(3)           NULL CONSTRAINT FK_OpeningBalanceImportRow_Currency REFERENCES core.Currency (Code),
    AmountCurrency  decimal(19,2)     NULL,
    OpeningDebit    decimal(19,2)     NOT NULL,
    OpeningCredit   decimal(19,2)     NOT NULL,
    TurnoverDebit   decimal(19,2)     NOT NULL,
    TurnoverCredit  decimal(19,2)     NOT NULL,
    ClosingDebit    decimal(19,2)     NOT NULL,
    ClosingCredit   decimal(19,2)     NOT NULL,
    CONSTRAINT UQ_OpeningBalanceImportRow_Import_Row UNIQUE (ImportId, RowNumber)
);
CREATE INDEX IX_OpeningBalanceImportRow_AccountId ON fin.OpeningBalanceImportRow (AccountId);
CREATE INDEX IX_OpeningBalanceImportRow_PartnerId ON fin.OpeningBalanceImportRow (PartnerId) WHERE PartnerId IS NOT NULL;
