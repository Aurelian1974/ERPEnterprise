-- 02.02 Contabilitate, C3.1: lista de verificari la inchiderea lunii (C3 3.3) si rularile ei. Pasii livrati vin din seed-ul 12; rezultatul
-- fiecarui pas al unei rulari se pastreaza cu problemele gasite (JSON, doar pentru afisare) si cu trecerea peste el (motiv, cine, cand).
CREATE TABLE fin.PeriodCloseStepDefinition (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PeriodCloseStepDefinition PRIMARY KEY,
    Code        varchar(40)       NOT NULL,
    Name        nvarchar(200)     NOT NULL,
    SortOrder   int               NOT NULL,
    IsSystem    bit               NOT NULL CONSTRAINT DF_PeriodCloseStepDefinition_IsSystem DEFAULT (1),
    IsActive    bit               NOT NULL CONSTRAINT DF_PeriodCloseStepDefinition_IsActive DEFAULT (1),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_PeriodCloseStepDefinition_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    RowVersion  rowversion        NOT NULL,
    CONSTRAINT UQ_PeriodCloseStepDefinition_Code UNIQUE (Code)
);

CREATE TABLE fin.PeriodCloseRun (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PeriodCloseRun PRIMARY KEY,
    CompanyId   int               NOT NULL CONSTRAINT FK_PeriodCloseRun_Company REFERENCES core.Company (Id),
    PeriodId    int               NOT NULL CONSTRAINT FK_PeriodCloseRun_Period REFERENCES fin.AccountingPeriod (Id),
    CanClose    bit               NOT NULL,
    StartedAt   datetime2(3)      NOT NULL CONSTRAINT DF_PeriodCloseRun_StartedAt DEFAULT (sysutcdatetime()),
    StartedBy   int               NOT NULL
);
CREATE INDEX IX_PeriodCloseRun_Period_StartedAt ON fin.PeriodCloseRun (PeriodId, StartedAt);
CREATE INDEX IX_PeriodCloseRun_CompanyId ON fin.PeriodCloseRun (CompanyId);

CREATE TABLE fin.PeriodCloseRunStep (
    Id              bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PeriodCloseRunStep PRIMARY KEY,
    RunId           bigint            NOT NULL CONSTRAINT FK_PeriodCloseRunStep_Run REFERENCES fin.PeriodCloseRun (Id) ON DELETE CASCADE,
    StepCode        varchar(40)       NOT NULL,
    Severity        tinyint           NOT NULL,
    AllowOverride   bit               NOT NULL,
    Outcome         tinyint           NOT NULL,
    Summary         nvarchar(400)     NOT NULL,
    Issues          nvarchar(max)     NULL,
    OverrideReason  nvarchar(500)     NULL,
    OverriddenBy    int               NULL,
    OverriddenAt    datetime2(3)      NULL,
    -- Severity: 1 blocant, 2 avertisment. Outcome: 1 in regula, 2 esuat, 3 nu se aplica.
    CONSTRAINT UQ_PeriodCloseRunStep_Run_Step UNIQUE (RunId, StepCode),
    CONSTRAINT CK_PeriodCloseRunStep_Severity CHECK (Severity IN (1, 2)),
    CONSTRAINT CK_PeriodCloseRunStep_Outcome CHECK (Outcome BETWEEN 1 AND 3),
    CONSTRAINT CK_PeriodCloseRunStep_Issues CHECK (Issues IS NULL OR ISJSON(Issues) = 1)
);
