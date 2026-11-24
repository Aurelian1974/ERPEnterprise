-- 02.02 Finante, F5.2b: instantaneele prognozei (F5 3.6), regulile de alerta (3.7) si categoria de cash-flow a tipului de operatiune (4.5).
-- Instantaneele nu se modifica dupa creare: fara ModifiedAt, ModifiedBy, RowVersion (abaterea scrisa in sectiunea 7). Regula de alerta
-- tine ziua ultimei alerte, ca alerta sa plece o singura data pe zi si regula (F5-A12).

CREATE TABLE fin.CashFlowSnapshot (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashFlowSnapshot PRIMARY KEY,
    CompanyId          int               NOT NULL CONSTRAINT FK_CashFlowSnapshot_Company REFERENCES core.Company (Id),
    Name               nvarchar(200)     NOT NULL,
    TakenAt            datetime2(3)      NOT NULL CONSTRAINT DF_CashFlowSnapshot_TakenAt DEFAULT (sysutcdatetime()),
    TakenBy            int               NOT NULL,
    StartDate          date              NOT NULL,
    HorizonFrom        date              NOT NULL,
    HorizonTo          date              NOT NULL,
    Granularity        tinyint           NOT NULL,
    TreasuryAccountId  int               NULL CONSTRAINT FK_CashFlowSnapshot_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    CurrencyCode       char(3)           NULL,
    IsAutomatic        bit               NOT NULL CONSTRAINT DF_CashFlowSnapshot_IsAutomatic DEFAULT (0),
    OpeningBalanceRon  decimal(19,2)     NOT NULL,
    Parameters         nvarchar(max)     NULL,
    CONSTRAINT CK_CashFlowSnapshot_Granularity CHECK (Granularity BETWEEN 1 AND 3),
    CONSTRAINT CK_CashFlowSnapshot_Horizon CHECK (HorizonTo >= HorizonFrom AND HorizonFrom >= StartDate),
    CONSTRAINT CK_CashFlowSnapshot_Parameters CHECK (Parameters IS NULL OR ISJSON(Parameters) = 1)
);
CREATE INDEX IX_CashFlowSnapshot_CompanyId_TakenAt ON fin.CashFlowSnapshot (CompanyId, TakenAt);
CREATE INDEX IX_CashFlowSnapshot_TreasuryAccountId ON fin.CashFlowSnapshot (TreasuryAccountId) WHERE TreasuryAccountId IS NOT NULL;

CREATE TABLE fin.CashFlowSnapshotLine (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashFlowSnapshotLine PRIMARY KEY,
    SnapshotId         int               NOT NULL CONSTRAINT FK_CashFlowSnapshotLine_Snapshot REFERENCES fin.CashFlowSnapshot (Id) ON DELETE CASCADE,
    PeriodStart        date              NOT NULL,
    CategoryId         int               NULL CONSTRAINT FK_CashFlowSnapshotLine_Category REFERENCES fin.CashFlowCategory (Id),
    TreasuryAccountId  int               NULL CONSTRAINT FK_CashFlowSnapshotLine_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    CurrencyCode       char(3)           NOT NULL,
    Amount             decimal(19,2)     NOT NULL,
    AmountRon          decimal(19,2)     NOT NULL,
    SourceKind         tinyint           NOT NULL,
    -- Soldul de pornire (SourceKind 1) nu are categorie; celelalte surse au.
    CONSTRAINT CK_CashFlowSnapshotLine_SourceKind CHECK (SourceKind BETWEEN 1 AND 5 AND (SourceKind = 1 OR CategoryId IS NOT NULL))
);
CREATE INDEX IX_CashFlowSnapshotLine_SnapshotId_PeriodStart ON fin.CashFlowSnapshotLine (SnapshotId, PeriodStart);
CREATE INDEX IX_CashFlowSnapshotLine_CategoryId ON fin.CashFlowSnapshotLine (CategoryId);
CREATE INDEX IX_CashFlowSnapshotLine_TreasuryAccountId ON fin.CashFlowSnapshotLine (TreasuryAccountId) WHERE TreasuryAccountId IS NOT NULL;

CREATE TABLE fin.CashFlowAlertRule (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashFlowAlertRule PRIMARY KEY,
    CompanyId          int               NOT NULL CONSTRAINT FK_CashFlowAlertRule_Company REFERENCES core.Company (Id),
    TreasuryAccountId  int               NULL CONSTRAINT FK_CashFlowAlertRule_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    MinBalanceRon      decimal(19,2)     NOT NULL,
    HorizonDays        smallint          NOT NULL CONSTRAINT DF_CashFlowAlertRule_HorizonDays DEFAULT (14),
    NotifyRoleId       int               NULL CONSTRAINT FK_CashFlowAlertRule_Role REFERENCES core.Role (Id),
    IsActive           bit               NOT NULL CONSTRAINT DF_CashFlowAlertRule_IsActive DEFAULT (1),
    LastAlertedOn      date              NULL,
    CreatedAt          datetime2(3)      NOT NULL CONSTRAINT DF_CashFlowAlertRule_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy          int               NOT NULL,
    ModifiedAt         datetime2(3)      NULL,
    ModifiedBy         int               NULL,
    RowVersion         rowversion        NOT NULL,
    CONSTRAINT CK_CashFlowAlertRule_HorizonDays CHECK (HorizonDays BETWEEN 1 AND 92)
);
CREATE INDEX IX_CashFlowAlertRule_CompanyId_IsActive ON fin.CashFlowAlertRule (CompanyId, IsActive);
CREATE INDEX IX_CashFlowAlertRule_TreasuryAccountId ON fin.CashFlowAlertRule (TreasuryAccountId) WHERE TreasuryAccountId IS NOT NULL;
CREATE INDEX IX_CashFlowAlertRule_NotifyRoleId ON fin.CashFlowAlertRule (NotifyRoleId) WHERE NotifyRoleId IS NOT NULL;

ALTER TABLE fin.TreasuryOperationType ADD CashFlowCategoryId int NULL
    CONSTRAINT FK_TreasuryOperationType_CashFlowCategory REFERENCES fin.CashFlowCategory (Id);
GO
CREATE INDEX IX_TreasuryOperationType_CashFlowCategoryId ON fin.TreasuryOperationType (CashFlowCategoryId) WHERE CashFlowCategoryId IS NOT NULL;
