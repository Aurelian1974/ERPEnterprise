-- 02.02 Contabilitate, C4.2: cadrul declaratiilor (C4 3.3, 3.7): formularele, declaratiile cu randurile si fisierele lor, calendarul fiscal pe
-- firma si obligatiile de plata. Status declaratie: 1 ciorna, 2 verificata, 3 generata, 4 depusa, 5 inlocuita; Kind: 1 initiala, 2 rectificativa.
-- Fisierele (XML, raportul validatorului, PDF, recipisa) stau in tabel, nu in atasamentele Nucleului (abatere, C4 nota C4.2).
CREATE TABLE fin.TaxForm (
    Code         varchar(10)    NOT NULL CONSTRAINT PK_TaxForm PRIMARY KEY,
    Name         nvarchar(200)  NOT NULL,
    BudgetKind   tinyint        NULL
);

CREATE TABLE fin.TaxReturn (
    Id                  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TaxReturn PRIMARY KEY,
    CompanyId           int               NOT NULL CONSTRAINT FK_TaxReturn_Company REFERENCES core.Company (Id),
    FormCode            varchar(10)       NOT NULL CONSTRAINT FK_TaxReturn_Form REFERENCES fin.TaxForm (Code),
    PeriodFrom          date              NOT NULL,
    PeriodTo            date              NOT NULL,
    Kind                tinyint           NOT NULL CONSTRAINT DF_TaxReturn_Kind DEFAULT (1),
    CorrectsReturnId    int               NULL CONSTRAINT FK_TaxReturn_Corrects REFERENCES fin.TaxReturn (Id),
    Status              tinyint           NOT NULL CONSTRAINT DF_TaxReturn_Status DEFAULT (1),
    NeedsReview         bit               NOT NULL CONSTRAINT DF_TaxReturn_NeedsReview DEFAULT (0),
    DueDate             date              NULL,
    CalculatedAt        datetime2(3)      NULL,
    CalculatedBy        int               NULL,
    CalculationHash     int               NULL,
    GeneratedAt         datetime2(3)      NULL,
    SubmittedAt         datetime2(3)      NULL,
    SubmittedBy         int               NULL,
    RegistrationNumber  nvarchar(50)      NULL,
    AmountDue           decimal(19,2)     NOT NULL CONSTRAINT DF_TaxReturn_AmountDue DEFAULT (0),
    AmountRefundable    decimal(19,2)     NOT NULL CONSTRAINT DF_TaxReturn_AmountRefundable DEFAULT (0),
    CreatedAt           datetime2(3)      NOT NULL CONSTRAINT DF_TaxReturn_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy           int               NOT NULL,
    ModifiedAt          datetime2(3)      NULL,
    ModifiedBy          int               NULL,
    RowVersion          rowversion        NOT NULL,
    CONSTRAINT CK_TaxReturn_Status CHECK (Status BETWEEN 1 AND 5),
    CONSTRAINT CK_TaxReturn_Kind CHECK (Kind IN (1, 2)),
    CONSTRAINT CK_TaxReturn_Period CHECK (PeriodTo >= PeriodFrom)
);
CREATE UNIQUE INDEX UX_TaxReturn_Company_Form_Period_Kind ON fin.TaxReturn (CompanyId, FormCode, PeriodFrom, Kind) WHERE Status <> 5;
CREATE INDEX IX_TaxReturn_Company_DueDate ON fin.TaxReturn (CompanyId, DueDate, Status);
CREATE INDEX IX_TaxReturn_CorrectsReturnId ON fin.TaxReturn (CorrectsReturnId) WHERE CorrectsReturnId IS NOT NULL;
CREATE INDEX IX_TaxReturn_FormCode ON fin.TaxReturn (FormCode);

CREATE TABLE fin.TaxReturnLine (
    Id               bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TaxReturnLine PRIMARY KEY,
    ReturnId         int               NOT NULL CONSTRAINT FK_TaxReturnLine_Return REFERENCES fin.TaxReturn (Id) ON DELETE CASCADE,
    RowCode          varchar(30)       NOT NULL,
    ColumnCode       varchar(20)       NOT NULL,
    Label            nvarchar(200)     NOT NULL,
    SortOrder        int               NOT NULL,
    IsManual         bit               NOT NULL CONSTRAINT DF_TaxReturnLine_IsManual DEFAULT (0),
    CalculatedValue  decimal(19,2)     NOT NULL,
    ManualValue      decimal(19,2)     NULL,
    ManualReason     nvarchar(500)     NULL,
    CONSTRAINT UQ_TaxReturnLine_Return_Row_Column UNIQUE (ReturnId, RowCode, ColumnCode)
);

CREATE TABLE fin.TaxReturnFile (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TaxReturnFile PRIMARY KEY,
    ReturnId   int               NOT NULL CONSTRAINT FK_TaxReturnFile_Return REFERENCES fin.TaxReturn (Id) ON DELETE CASCADE,
    Kind       tinyint           NOT NULL,
    FileName   nvarchar(260)     NOT NULL,
    Content    varbinary(max)    NOT NULL,
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_TaxReturnFile_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL,
    -- Kind: 1 fisierul declaratiei, 2 raportul validatorului, 3 PDF, 4 recipisa.
    CONSTRAINT CK_TaxReturnFile_Kind CHECK (Kind BETWEEN 1 AND 4)
);
CREATE INDEX IX_TaxReturnFile_ReturnId ON fin.TaxReturnFile (ReturnId);

CREATE TABLE fin.TaxCalendarItem (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TaxCalendarItem PRIMARY KEY,
    CompanyId   int               NOT NULL CONSTRAINT FK_TaxCalendarItem_Company REFERENCES core.Company (Id),
    FormCode    varchar(10)       NOT NULL CONSTRAINT FK_TaxCalendarItem_Form REFERENCES fin.TaxForm (Code),
    PeriodFrom  date              NOT NULL,
    PeriodTo    date              NOT NULL,
    DueDate     date              NOT NULL,
    CONSTRAINT UQ_TaxCalendarItem_Company_Form_Period UNIQUE (CompanyId, FormCode, PeriodFrom)
);
CREATE INDEX IX_TaxCalendarItem_DueDate ON fin.TaxCalendarItem (DueDate);
CREATE INDEX IX_TaxCalendarItem_FormCode ON fin.TaxCalendarItem (FormCode);

CREATE TABLE fin.TaxObligation (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TaxObligation PRIMARY KEY,
    CompanyId        int               NOT NULL CONSTRAINT FK_TaxObligation_Company REFERENCES core.Company (Id),
    ReturnId         int               NOT NULL CONSTRAINT FK_TaxObligation_Return REFERENCES fin.TaxReturn (Id),
    BudgetKind       tinyint           NOT NULL,
    Amount           decimal(19,2)     NOT NULL,
    DueDate          date              NOT NULL,
    PlanItemId       int               NULL CONSTRAINT FK_TaxObligation_PlanItem REFERENCES fin.CashFlowPlanItem (Id),
    CreatedAt        datetime2(3)      NOT NULL CONSTRAINT DF_TaxObligation_CreatedAt DEFAULT (sysutcdatetime()),
    -- BudgetKind: 1 TVA, 2 impozit pe profit, 3 impozit pe dividende.
    CONSTRAINT CK_TaxObligation_BudgetKind CHECK (BudgetKind BETWEEN 1 AND 3)
);
CREATE INDEX IX_TaxObligation_Company_DueDate ON fin.TaxObligation (CompanyId, DueDate);
CREATE INDEX IX_TaxObligation_ReturnId ON fin.TaxObligation (ReturnId);
CREATE INDEX IX_TaxObligation_PlanItemId ON fin.TaxObligation (PlanItemId) WHERE PlanItemId IS NOT NULL;
