-- Documentele de casa (02.02, F1 3.3, 3.4, 3.6): registrul zilnic al fiecarei case, documentele (chitanta, dispozitie de incasare,
-- dispozitie de plata, sold initial) cu liniile lor, plafoanele de numerar cu data de vigoare (P6) si jurnalul verificarilor refuzate.
-- Legaturile spre avansuri si deconturi (CashAdvanceId, ExpenseReportId) primesc FK odata cu tabelele lor (F1.5).

-- Registrul de casa: un rand pe casa si zi cu activitate sau inchisa explicit. Soldul final e calculat (o singura sursa).
CREATE TABLE fin.CashRegisterDay (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashRegisterDay PRIMARY KEY,
    TreasuryAccountId int               NOT NULL CONSTRAINT FK_CashRegisterDay_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    [Day]             date              NOT NULL,
    OpeningBalance    decimal(19,2)     NOT NULL,
    TotalIn           decimal(19,2)     NOT NULL CONSTRAINT DF_CashRegisterDay_TotalIn DEFAULT (0),
    TotalOut          decimal(19,2)     NOT NULL CONSTRAINT DF_CashRegisterDay_TotalOut DEFAULT (0),
    ClosingBalance    AS (OpeningBalance + TotalIn - TotalOut) PERSISTED,
    Status            tinyint           NOT NULL CONSTRAINT DF_CashRegisterDay_Status DEFAULT (1),
    CloseMode         tinyint           NOT NULL CONSTRAINT DF_CashRegisterDay_CloseMode DEFAULT (1),
    FirstPageNo       int               NULL,
    LastPageNo        int               NULL,
    ClosedAt          datetime2(3)      NULL,
    ClosedBy          int               NULL,
    ReopenedAt        datetime2(3)      NULL,
    ReopenedBy        int               NULL,
    ReopenReason      nvarchar(500)     NULL,
    CreatedAt         datetime2(3)      NOT NULL CONSTRAINT DF_CashRegisterDay_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int               NOT NULL,
    ModifiedAt        datetime2(3)      NULL,
    ModifiedBy        int               NULL,
    RowVersion        rowversion        NOT NULL,
    CONSTRAINT UQ_CashRegisterDay_TreasuryAccountId_Day UNIQUE (TreasuryAccountId, [Day]),
    CONSTRAINT CK_CashRegisterDay_Status CHECK (Status IN (1, 2)),
    CONSTRAINT CK_CashRegisterDay_CloseMode CHECK (CloseMode IN (1, 2))
);
GO

CREATE INDEX IX_CashRegisterDay_TreasuryAccountId_Status_Day ON fin.CashRegisterDay (TreasuryAccountId, Status, [Day]);
GO

-- Documentul de casa: Kind 1 Chitanta, 2 Dispozitie de incasare, 3 Dispozitie de plata, 4 Sold initial. Status 1 Ciorna, 2 Validat,
-- 3 Stornat. Storno = document nou cu sume negative (ReversalOfId), validat in ziua curenta; doar storno, fara anulare (decizia din 15.11.2026).
CREATE TABLE fin.CashDocument (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashDocument PRIMARY KEY,
    PublicId          uniqueidentifier  NOT NULL CONSTRAINT DF_CashDocument_PublicId DEFAULT (NEWID()),
    CompanyId         int               NOT NULL CONSTRAINT FK_CashDocument_Company REFERENCES core.Company (Id),
    TreasuryAccountId int               NOT NULL CONSTRAINT FK_CashDocument_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    Kind              tinyint           NOT NULL,
    Number            nvarchar(30)      NULL,
    DocumentDate      date              NOT NULL,
    Status            tinyint           NOT NULL CONSTRAINT DF_CashDocument_Status DEFAULT (1),
    PartnerId         int               NULL CONSTRAINT FK_CashDocument_Partner REFERENCES core.Partner (Id),
    EmployeeId        int               NULL CONSTRAINT FK_CashDocument_Employee REFERENCES core.Employee (Id),
    PersonName        nvarchar(200)     NULL,
    PersonIdEncrypted varbinary(512)    NULL,
    CurrencyCode      char(3)           NOT NULL CONSTRAINT FK_CashDocument_Currency REFERENCES core.Currency (Code),
    ExchangeRate      decimal(19,6)     NOT NULL,
    RateDate          date              NOT NULL,
    TotalAmount       decimal(19,2)     NOT NULL,
    TotalAmountRon    decimal(19,2)     NOT NULL,
    Description       nvarchar(500)     NULL,
    CashRegisterDayId int               NULL CONSTRAINT FK_CashDocument_CashRegisterDay REFERENCES fin.CashRegisterDay (Id),
    ReversalOfId      int               NULL CONSTRAINT FK_CashDocument_ReversalOf REFERENCES fin.CashDocument (Id),
    ReversedById      int               NULL CONSTRAINT FK_CashDocument_ReversedBy REFERENCES fin.CashDocument (Id),
    CashAdvanceId     int               NULL,
    ExpenseReportId   int               NULL,
    ConfirmedWarnings bit               NOT NULL CONSTRAINT DF_CashDocument_ConfirmedWarnings DEFAULT (0),
    ValidatedAt       datetime2(3)      NULL,
    ValidatedBy       int               NULL,
    CreatedAt         datetime2(3)      NOT NULL CONSTRAINT DF_CashDocument_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int               NOT NULL,
    ModifiedAt        datetime2(3)      NULL,
    ModifiedBy        int               NULL,
    RowVersion        rowversion        NOT NULL,
    CONSTRAINT CK_CashDocument_Kind CHECK (Kind IN (1, 2, 3, 4)),
    CONSTRAINT CK_CashDocument_Status CHECK (Status IN (1, 2, 3)),
    CONSTRAINT CK_CashDocument_OnePerson CHECK (
        (CASE WHEN PartnerId IS NOT NULL THEN 1 ELSE 0 END)
      + (CASE WHEN EmployeeId IS NOT NULL THEN 1 ELSE 0 END)
      + (CASE WHEN PersonName IS NOT NULL THEN 1 ELSE 0 END) <= 1),
    CONSTRAINT CK_CashDocument_ReceiptHasPayer CHECK (Kind <> 1 OR PartnerId IS NOT NULL OR PersonName IS NOT NULL),
    CONSTRAINT CK_CashDocument_PostedHasNumber CHECK (Status = 1 OR Kind = 4 OR Number IS NOT NULL),
    CONSTRAINT CK_CashDocument_ExchangeRate CHECK (ExchangeRate > 0)
);
GO

CREATE UNIQUE INDEX UX_CashDocument_TreasuryAccountId_Kind_Number ON fin.CashDocument (TreasuryAccountId, Kind, Number) WHERE Number IS NOT NULL;
CREATE UNIQUE INDEX UX_CashDocument_PublicId ON fin.CashDocument (PublicId);
CREATE UNIQUE INDEX UX_CashDocument_ReversalOfId ON fin.CashDocument (ReversalOfId) WHERE ReversalOfId IS NOT NULL;
CREATE INDEX IX_CashDocument_CompanyId_DocumentDate ON fin.CashDocument (CompanyId, DocumentDate);
CREATE INDEX IX_CashDocument_PartnerId_DocumentDate ON fin.CashDocument (PartnerId, DocumentDate) INCLUDE (TotalAmountRon, Status, Kind)
    WHERE PartnerId IS NOT NULL;
CREATE INDEX IX_CashDocument_TreasuryAccountId_DocumentDate_Status ON fin.CashDocument (TreasuryAccountId, DocumentDate, Status);
CREATE INDEX IX_CashDocument_CashRegisterDayId ON fin.CashDocument (CashRegisterDayId) WHERE CashRegisterDayId IS NOT NULL;
CREATE INDEX IX_CashDocument_EmployeeId ON fin.CashDocument (EmployeeId) WHERE EmployeeId IS NOT NULL;
GO

CREATE TABLE fin.CashDocumentLine (
    Id                       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashDocumentLine PRIMARY KEY,
    CashDocumentId           int               NOT NULL,
    LineNumber               smallint          NOT NULL,
    TreasuryOperationTypeId  int               NOT NULL CONSTRAINT FK_CashDocumentLine_TreasuryOperationType REFERENCES fin.TreasuryOperationType (Id),
    Amount                   decimal(19,2)     NOT NULL,
    AmountRon                decimal(19,2)     NOT NULL,
    CounterTreasuryAccountId int               NULL CONSTRAINT FK_CashDocumentLine_CounterTreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    SettlementRef            nvarchar(100)     NULL,
    CostCenterId             int               NULL CONSTRAINT FK_CashDocumentLine_CostCenter REFERENCES core.DimensionValue (Id),
    ProjectId                int               NULL CONSTRAINT FK_CashDocumentLine_Project REFERENCES core.DimensionValue (Id),
    Note                     nvarchar(500)     NULL,
    -- Liniile se sterg cu ciorna lor (SQL-15: cascada doar pe ciorne; documentele validate nu se sterg).
    CONSTRAINT FK_CashDocumentLine_CashDocument FOREIGN KEY (CashDocumentId) REFERENCES fin.CashDocument (Id) ON DELETE CASCADE,
    CONSTRAINT UQ_CashDocumentLine_CashDocumentId_LineNumber UNIQUE (CashDocumentId, LineNumber)
);
GO

CREATE INDEX IX_CashDocumentLine_CounterTreasuryAccountId ON fin.CashDocumentLine (CounterTreasuryAccountId) WHERE CounterTreasuryAccountId IS NOT NULL;
GO

-- Plafoanele de numerar (Legea 70/2015), cu data de vigoare (P6). CompanyId NULL = plafon legal livrat (se schimba doar prin versiune);
-- altfel plafon propriu al firmei (Fin.CashLimit.Manage). Action: 1 Blocare, 2 Avertizare.
CREATE TABLE fin.CashLimitRule (
    Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashLimitRule PRIMARY KEY,
    CompanyId      int               NULL CONSTRAINT FK_CashLimitRule_Company REFERENCES core.Company (Id),
    Category       varchar(20)       NOT NULL,
    CurrencyCode   char(3)           NOT NULL CONSTRAINT FK_CashLimitRule_Currency REFERENCES core.Currency (Code),
    Amount         decimal(19,2)     NOT NULL,
    Action         tinyint           NOT NULL,
    ValidFrom      date              NOT NULL,
    ValidTo        date              NULL,
    LegalReference nvarchar(200)     NULL,
    CreatedAt      datetime2(3)      NOT NULL CONSTRAINT DF_CashLimitRule_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy      int               NOT NULL,
    ModifiedAt     datetime2(3)      NULL,
    ModifiedBy     int               NULL,
    RowVersion     rowversion        NOT NULL,
    CONSTRAINT CK_CashLimitRule_Category CHECK (Category IN ('IncasarePJ', 'IncasarePF', 'PlataPJ', 'PlataPF', 'AvansPersoanaZi', 'SoldCasa')),
    CONSTRAINT CK_CashLimitRule_Action CHECK (Action IN (1, 2)),
    CONSTRAINT CK_CashLimitRule_Amount CHECK (Amount > 0),
    CONSTRAINT CK_CashLimitRule_Validity CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
GO

CREATE UNIQUE INDEX UX_CashLimitRule_Company_Category_Currency_ValidFrom ON fin.CashLimitRule (CompanyId, Category, CurrencyCode, ValidFrom);
GO

-- Verificarile de plafon refuzate sau confirmate (F1 3.6 regula 4), pentru raportul incercarilor de depasire.
CREATE TABLE fin.CashLimitCheck (
    Id             bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashLimitCheck PRIMARY KEY,
    CompanyId      int               NOT NULL CONSTRAINT FK_CashLimitCheck_Company REFERENCES core.Company (Id),
    CashDocumentId int               NULL CONSTRAINT FK_CashLimitCheck_CashDocument REFERENCES fin.CashDocument (Id),
    CheckedAt      datetime2(3)      NOT NULL CONSTRAINT DF_CashLimitCheck_CheckedAt DEFAULT (sysutcdatetime()),
    UserId         int               NOT NULL CONSTRAINT FK_CashLimitCheck_User REFERENCES core.[User] (Id),
    PartnerId      int               NULL CONSTRAINT FK_CashLimitCheck_Partner REFERENCES core.Partner (Id),
    EmployeeId     int               NULL,
    PersonName     nvarchar(200)     NULL,
    Category       varchar(20)       NOT NULL,
    DocumentDate   date              NOT NULL,
    Amount         decimal(19,2)     NOT NULL,
    DayTotal       decimal(19,2)     NOT NULL,
    LimitAmount    decimal(19,2)     NOT NULL,
    Action         tinyint           NOT NULL,
    Confirmed      bit               NOT NULL CONSTRAINT DF_CashLimitCheck_Confirmed DEFAULT (0)
);
GO

CREATE INDEX IX_CashLimitCheck_CompanyId_CheckedAt ON fin.CashLimitCheck (CompanyId, CheckedAt);
GO

-- Liniile unei ciorne (fin.usp_CashDocument_Save): inlocuiesc liniile documentului.
CREATE TYPE fin.CashDocumentLineList AS TABLE (
    LineNumber               smallint      NOT NULL PRIMARY KEY,
    TreasuryOperationTypeId  int           NOT NULL,
    Amount                   decimal(19,2) NOT NULL,
    AmountRon                decimal(19,2) NOT NULL,
    CounterTreasuryAccountId int           NULL,
    SettlementRef            nvarchar(100) NULL,
    CostCenterId             int           NULL,
    ProjectId                int           NULL,
    Note                     nvarchar(500) NULL
);
GO
