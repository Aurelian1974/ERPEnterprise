-- 02.02 Contabilitate, C3a: anii financiari si lunile contabile ale fiecarei firme (C3 3.1, 3.2), cu istoricul starilor. Luna: 1 deschisa,
-- 2 inchisa operativ (modulele nu mai valideaza documente; contabilitatea mai poate scrie note), 3 inchisa, 4 blocata. Anul: 1 deschis,
-- 2 inchis, 3 blocat. Lunile se inchid in ordine si se redeschid de la ultima spre prima (C3 2, regula 1).

CREATE TABLE fin.FiscalYear (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FiscalYear PRIMARY KEY,
    CompanyId       int               NOT NULL CONSTRAINT FK_FiscalYear_Company REFERENCES core.Company (Id),
    [Year]          smallint          NOT NULL,
    StartDate       date              NOT NULL,
    EndDate         date              NOT NULL,
    Status          tinyint           NOT NULL CONSTRAINT DF_FiscalYear_Status DEFAULT (1),
    VatPeriodicity  tinyint           NOT NULL CONSTRAINT DF_FiscalYear_VatPeriodicity DEFAULT (1),
    ClosedAt        datetime2(3)      NULL,
    ClosedBy        int               NULL,
    LockedAt        datetime2(3)      NULL,
    LockedBy        int               NULL,
    LockReason      nvarchar(500)     NULL,
    CreatedAt       datetime2(3)      NOT NULL CONSTRAINT DF_FiscalYear_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy       int               NOT NULL,
    ModifiedAt      datetime2(3)      NULL,
    ModifiedBy      int               NULL,
    RowVersion      rowversion        NOT NULL,
    CONSTRAINT UQ_FiscalYear_Company_Year UNIQUE (CompanyId, [Year]),
    CONSTRAINT CK_FiscalYear_Status CHECK (Status BETWEEN 1 AND 3),
    CONSTRAINT CK_FiscalYear_VatPeriodicity CHECK (VatPeriodicity IN (1, 3, 6, 12)),
    CONSTRAINT CK_FiscalYear_Dates CHECK (EndDate > StartDate AND DATEDIFF(MONTH, StartDate, EndDate) <= 12)
);

CREATE TABLE fin.AccountingPeriod (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountingPeriod PRIMARY KEY,
    CompanyId        int               NOT NULL CONSTRAINT FK_AccountingPeriod_Company REFERENCES core.Company (Id),
    FiscalYearId     int               NOT NULL CONSTRAINT FK_AccountingPeriod_FiscalYear REFERENCES fin.FiscalYear (Id),
    PeriodNo         tinyint           NOT NULL,
    StartDate        date              NOT NULL,
    EndDate          date              NOT NULL,
    Status           tinyint           NOT NULL CONSTRAINT DF_AccountingPeriod_Status DEFAULT (1),
    IsImported       bit               NOT NULL CONSTRAINT DF_AccountingPeriod_IsImported DEFAULT (0),
    SoftClosedAt     datetime2(3)      NULL,
    SoftClosedBy     int               NULL,
    ClosedAt         datetime2(3)      NULL,
    ClosedBy         int               NULL,
    LastCloseRunId   int               NULL,
    CreatedAt        datetime2(3)      NOT NULL CONSTRAINT DF_AccountingPeriod_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int               NOT NULL,
    ModifiedAt       datetime2(3)      NULL,
    ModifiedBy       int               NULL,
    RowVersion       rowversion        NOT NULL,
    CONSTRAINT UQ_AccountingPeriod_Year_No UNIQUE (FiscalYearId, PeriodNo),
    CONSTRAINT CK_AccountingPeriod_PeriodNo CHECK (PeriodNo BETWEEN 1 AND 12),
    CONSTRAINT CK_AccountingPeriod_Status CHECK (Status BETWEEN 1 AND 4),
    CONSTRAINT CK_AccountingPeriod_Dates CHECK (EndDate >= StartDate)
);
CREATE UNIQUE INDEX UX_AccountingPeriod_Company_StartDate ON fin.AccountingPeriod (CompanyId, StartDate) INCLUDE (EndDate, Status);
CREATE INDEX IX_AccountingPeriod_FiscalYearId ON fin.AccountingPeriod (FiscalYearId);

CREATE TABLE fin.PeriodStatusHistory (
    Id           bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PeriodStatusHistory PRIMARY KEY,
    PeriodId     int               NOT NULL CONSTRAINT FK_PeriodStatusHistory_Period REFERENCES fin.AccountingPeriod (Id),
    FromStatus   tinyint           NOT NULL,
    ToStatus     tinyint           NOT NULL,
    Reason       nvarchar(500)     NULL,
    ChangedAt    datetime2(3)      NOT NULL CONSTRAINT DF_PeriodStatusHistory_ChangedAt DEFAULT (sysutcdatetime()),
    ChangedBy    int               NOT NULL
);
CREATE INDEX IX_PeriodStatusHistory_PeriodId ON fin.PeriodStatusHistory (PeriodId, ChangedAt);
