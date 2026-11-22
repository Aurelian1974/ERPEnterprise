-- Conturile de trezorerie ale firmei (02.02, F1 3.1 si 3.2; macheta f1-01): banci, case, carduri, viramente interne, depozite;
-- casierii atribuiti caselor si tipurile de operatiuni de trezorerie (nomenclator extensibil, comun casei si bancii).
-- Codul contului e unic pe firma, fara diferenta de majuscule (SQL-24). Un cont folosit se dezactiveaza, nu se sterge (SQL-20).
CREATE TABLE fin.TreasuryAccount (
    Id                   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TreasuryAccount PRIMARY KEY,
    CompanyId            int               NOT NULL CONSTRAINT FK_TreasuryAccount_Company REFERENCES core.Company (Id),
    BranchId             int               NULL,
    Code                 nvarchar(30)      NOT NULL,
    Name                 nvarchar(200)     NOT NULL,
    Kind                 tinyint           NOT NULL,
    CurrencyCode         char(3)           NOT NULL CONSTRAINT FK_TreasuryAccount_Currency REFERENCES core.Currency (Code),
    CompanyBankAccountId int               NULL CONSTRAINT FK_TreasuryAccount_CompanyBankAccount REFERENCES core.CompanyBankAccount (Id),
    GlAccountCode        nvarchar(20)      NULL,
    ResponsibleUserId    int               NULL CONSTRAINT FK_TreasuryAccount_ResponsibleUser REFERENCES core.[User] (Id),
    ReceiptSeriesId      int               NULL CONSTRAINT FK_TreasuryAccount_ReceiptSeries REFERENCES core.NumberSeries (Id),
    CashInSeriesId       int               NULL CONSTRAINT FK_TreasuryAccount_CashInSeries REFERENCES core.NumberSeries (Id),
    CashOutSeriesId      int               NULL CONSTRAINT FK_TreasuryAccount_CashOutSeries REFERENCES core.NumberSeries (Id),
    MaxClosingBalance    decimal(19,2)     NULL,
    CloseMode            tinyint           NOT NULL CONSTRAINT DF_TreasuryAccount_CloseMode DEFAULT (1),
    OpeningDate          date              NULL,
    IsActive             bit               NOT NULL CONSTRAINT DF_TreasuryAccount_IsActive DEFAULT (1),
    CreatedAt            datetime2(3)      NOT NULL CONSTRAINT DF_TreasuryAccount_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy            int               NOT NULL,
    ModifiedAt           datetime2(3)      NULL,
    ModifiedBy           int               NULL,
    RowVersion           rowversion        NOT NULL,
    CodeKey              AS UPPER(Code) PERSISTED,
    CONSTRAINT FK_TreasuryAccount_Branch FOREIGN KEY (BranchId, CompanyId) REFERENCES core.Branch (Id, CompanyId),
    CONSTRAINT CK_TreasuryAccount_Kind CHECK (Kind BETWEEN 1 AND 5),
    CONSTRAINT CK_TreasuryAccount_CloseMode CHECK (CloseMode IN (1, 2)),
    CONSTRAINT CK_TreasuryAccount_BankAccount CHECK (Kind NOT IN (1, 3) OR CompanyBankAccountId IS NOT NULL),
    CONSTRAINT CK_TreasuryAccount_CashBranch CHECK (Kind <> 2 OR BranchId IS NOT NULL),
    CONSTRAINT CK_TreasuryAccount_MaxClosingBalance CHECK (MaxClosingBalance IS NULL OR MaxClosingBalance > 0)
);
GO

CREATE UNIQUE INDEX UX_TreasuryAccount_CompanyId_CodeKey ON fin.TreasuryAccount (CompanyId, CodeKey);
CREATE INDEX IX_TreasuryAccount_CompanyId_Kind_IsActive ON fin.TreasuryAccount (CompanyId, Kind, IsActive);
CREATE INDEX IX_TreasuryAccount_BranchId ON fin.TreasuryAccount (BranchId) WHERE BranchId IS NOT NULL;
CREATE INDEX IX_TreasuryAccount_CompanyBankAccountId ON fin.TreasuryAccount (CompanyBankAccountId) WHERE CompanyBankAccountId IS NOT NULL;
GO

-- Casierii unei case (F1 3.1): casierul lucreaza doar pe casele la care e atribuit (regula 7 din F1 2).
CREATE TABLE fin.TreasuryAccountUser (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TreasuryAccountUser PRIMARY KEY,
    TreasuryAccountId int               NOT NULL CONSTRAINT FK_TreasuryAccountUser_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    UserId            int               NOT NULL CONSTRAINT FK_TreasuryAccountUser_User REFERENCES core.[User] (Id),
    CanValidate       bit               NOT NULL CONSTRAINT DF_TreasuryAccountUser_CanValidate DEFAULT (1),
    ValidFrom         date              NULL,
    ValidTo           date              NULL,
    CreatedAt         datetime2(3)      NOT NULL CONSTRAINT DF_TreasuryAccountUser_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int               NOT NULL,
    ModifiedAt        datetime2(3)      NULL,
    ModifiedBy        int               NULL,
    RowVersion        rowversion        NOT NULL,
    CONSTRAINT UQ_TreasuryAccountUser_TreasuryAccountId_UserId UNIQUE (TreasuryAccountId, UserId),
    CONSTRAINT CK_TreasuryAccountUser_Validity CHECK (ValidTo IS NULL OR ValidFrom IS NULL OR ValidTo >= ValidFrom)
);
GO

CREATE INDEX IX_TreasuryAccountUser_UserId ON fin.TreasuryAccountUser (UserId);
GO

-- Tipurile de operatiuni de trezorerie (F1 3.2): tabela, nu enum (SQL-10). Tipurile livrate (IsSystem) nu se sterg si nu isi schimba sensul.
CREATE TABLE fin.TreasuryOperationType (
    Id                    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TreasuryOperationType PRIMARY KEY,
    Code                  nvarchar(30)      NOT NULL,
    Name                  nvarchar(200)     NOT NULL,
    Direction             tinyint           NOT NULL,
    RequiresPartner       bit               NOT NULL CONSTRAINT DF_TreasuryOperationType_RequiresPartner DEFAULT (0),
    RequiresEmployee      bit               NOT NULL CONSTRAINT DF_TreasuryOperationType_RequiresEmployee DEFAULT (0),
    RequiresCounterAccount bit              NOT NULL CONSTRAINT DF_TreasuryOperationType_RequiresCounterAccount DEFAULT (0),
    SettlementKind        tinyint           NOT NULL CONSTRAINT DF_TreasuryOperationType_SettlementKind DEFAULT (0),
    CounterGlAccountCode  nvarchar(20)      NULL,
    CashLimitCategory     varchar(20)       NULL,
    AppliesToCash         bit               NOT NULL CONSTRAINT DF_TreasuryOperationType_AppliesToCash DEFAULT (1),
    AppliesToBank         bit               NOT NULL CONSTRAINT DF_TreasuryOperationType_AppliesToBank DEFAULT (0),
    IsSystem              bit               NOT NULL CONSTRAINT DF_TreasuryOperationType_IsSystem DEFAULT (0),
    IsActive              bit               NOT NULL CONSTRAINT DF_TreasuryOperationType_IsActive DEFAULT (1),
    CreatedAt             datetime2(3)      NOT NULL CONSTRAINT DF_TreasuryOperationType_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy             int               NOT NULL,
    ModifiedAt            datetime2(3)      NULL,
    ModifiedBy            int               NULL,
    RowVersion            rowversion        NOT NULL,
    CodeKey               AS UPPER(Code) PERSISTED,
    CONSTRAINT CK_TreasuryOperationType_Direction CHECK (Direction IN (1, 2)),
    CONSTRAINT CK_TreasuryOperationType_SettlementKind CHECK (SettlementKind BETWEEN 0 AND 4),
    CONSTRAINT CK_TreasuryOperationType_AppliesTo CHECK (AppliesToCash = 1 OR AppliesToBank = 1)
);
GO

CREATE UNIQUE INDEX UX_TreasuryOperationType_CodeKey ON fin.TreasuryOperationType (CodeKey);
GO

-- Lista casierilor din salvarea fisei (fin.usp_TreasuryAccount_Save): inlocuieste atribuirile casei.
CREATE TYPE fin.TreasuryCashierList AS TABLE (
    UserId      int  NOT NULL PRIMARY KEY,
    CanValidate bit  NOT NULL,
    ValidFrom   date NULL,
    ValidTo     date NULL
);
GO
