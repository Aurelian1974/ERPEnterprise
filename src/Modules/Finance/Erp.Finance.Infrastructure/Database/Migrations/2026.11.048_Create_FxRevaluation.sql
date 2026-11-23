-- Reevaluarea valutara lunara (02.02, F4 3.6, 4.4, 4.5, 7): rularea pe luna si firma (1 Ciorna, 2 Validat, 3 Stornat; o singura rulare
-- validata pe luna), cursurile copiate din Nucleu si liniile (rezultatul calculului, fara audit de modificare: abaterea din F4 7). La
-- validare, partea din diferenta care revine platilor cu data dupa sfarsitul lunii, inregistrate deja, nu se adauga la valoarea contabila, ci
-- corecteaza diferenta lor realizata (randuri FxDifference legate de rulare).
CREATE TABLE fin.FxRevaluationRun (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FxRevaluationRun PRIMARY KEY,
    CompanyId        int               NOT NULL CONSTRAINT FK_FxRevaluationRun_Company REFERENCES core.Company (Id),
    PeriodYear       smallint          NOT NULL,
    PeriodMonth      tinyint           NOT NULL,
    RevaluationDate  date              NOT NULL,
    RateDate         date              NOT NULL,
    Number           nvarchar(40)      NOT NULL,
    Status           tinyint           NOT NULL CONSTRAINT DF_FxRevaluationRun_Status DEFAULT (1),
    IncludesAdvances bit               NOT NULL,
    TotalGainRon     decimal(19,2)     NOT NULL CONSTRAINT DF_FxRevaluationRun_TotalGainRon DEFAULT (0),
    TotalLossRon     decimal(19,2)     NOT NULL CONSTRAINT DF_FxRevaluationRun_TotalLossRon DEFAULT (0),
    GeneratedAt      datetime2(3)      NOT NULL,
    ValidatedAt      datetime2(3)      NULL,
    ValidatedBy      int               NULL,
    ReversedAt       datetime2(3)      NULL,
    ReversedBy       int               NULL,
    CreatedAt        datetime2(3)      NOT NULL CONSTRAINT DF_FxRevaluationRun_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int               NOT NULL,
    ModifiedAt       datetime2(3)      NULL,
    ModifiedBy       int               NULL,
    RowVersion       rowversion        NOT NULL,
    CONSTRAINT UQ_FxRevaluationRun_Number UNIQUE (CompanyId, Number),
    CONSTRAINT CK_FxRevaluationRun_Status CHECK (Status BETWEEN 1 AND 3),
    CONSTRAINT CK_FxRevaluationRun_Month CHECK (PeriodMonth BETWEEN 1 AND 12)
);
CREATE UNIQUE INDEX UX_FxRevaluationRun_Validated ON fin.FxRevaluationRun (CompanyId, PeriodYear, PeriodMonth) WHERE Status = 2;
CREATE UNIQUE INDEX UX_FxRevaluationRun_Draft ON fin.FxRevaluationRun (CompanyId, PeriodYear, PeriodMonth) WHERE Status = 1;
CREATE INDEX IX_FxRevaluationRun_CompanyId_Status ON fin.FxRevaluationRun (CompanyId, Status);

CREATE TABLE fin.FxRevaluationRate (
    Id            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FxRevaluationRate PRIMARY KEY,
    RunId         int               NOT NULL CONSTRAINT FK_FxRevaluationRate_Run REFERENCES fin.FxRevaluationRun (Id),
    CurrencyCode  char(3)           NOT NULL CONSTRAINT FK_FxRevaluationRate_Currency REFERENCES core.Currency (Code),
    Rate          decimal(19,6)     NOT NULL,
    Multiplier    int               NOT NULL,
    RateDate      date              NOT NULL,
    Source        varchar(10)       NOT NULL,
    CONSTRAINT UQ_FxRevaluationRate_RunId_CurrencyCode UNIQUE (RunId, CurrencyCode)
);

CREATE TABLE fin.FxRevaluationLine (
    Id                    bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_FxRevaluationLine PRIMARY KEY,
    RunId                 int                NOT NULL CONSTRAINT FK_FxRevaluationLine_FxRevaluationRun REFERENCES fin.FxRevaluationRun (Id),
    ElementKind           tinyint            NOT NULL,
    OpenItemId            bigint             NULL CONSTRAINT FK_FxRevaluationLine_OpenItem REFERENCES fin.OpenItem (Id),
    TreasuryAccountId     int                NULL CONSTRAINT FK_FxRevaluationLine_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    CommercialPaperId     int                NULL CONSTRAINT FK_FxRevaluationLine_CommercialPaper REFERENCES fin.CommercialPaper (Id),
    PartnerId             int                NULL CONSTRAINT FK_FxRevaluationLine_Partner REFERENCES core.Partner (Id),
    Role                  tinyint            NULL,
    CurrencyCode          char(3)            NOT NULL CONSTRAINT FK_FxRevaluationLine_Currency REFERENCES core.Currency (Code),
    IsIndexed             bit                NOT NULL CONSTRAINT DF_FxRevaluationLine_IsIndexed DEFAULT (0),
    ForeignAmount         decimal(19,2)      NOT NULL,
    Rate                  decimal(19,6)      NOT NULL,
    CarryingAmountRon     decimal(19,2)      NOT NULL,
    RevaluedAmountRon     decimal(19,2)      NOT NULL,
    DifferenceRon         decimal(19,2)      NOT NULL,
    AppliedDifferenceRon  decimal(19,2)      NULL,
    PreviousCarryingRate  decimal(19,6)      NULL,
    PreviousRunId         int                NULL,
    GlAccountCode         nvarchar(20)       NOT NULL,
    CounterGlAccountCode  nvarchar(20)       NULL,
    CreatedAt             datetime2(3)       NOT NULL CONSTRAINT DF_FxRevaluationLine_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy             int                NOT NULL,
    CONSTRAINT CK_FxRevaluationLine_ElementKind CHECK (ElementKind IN (1, 2, 3)),
    CONSTRAINT CK_FxRevaluationLine_OneElement CHECK (
        (ElementKind = 1 AND OpenItemId IS NOT NULL AND TreasuryAccountId IS NULL AND CommercialPaperId IS NULL) OR
        (ElementKind = 2 AND TreasuryAccountId IS NOT NULL AND OpenItemId IS NULL AND CommercialPaperId IS NULL) OR
        (ElementKind = 3 AND CommercialPaperId IS NOT NULL AND OpenItemId IS NULL AND TreasuryAccountId IS NULL))
);
CREATE INDEX IX_FxRevaluationLine_RunId ON fin.FxRevaluationLine (RunId) INCLUDE (CurrencyCode, DifferenceRon, GlAccountCode);
CREATE INDEX IX_FxRevaluationLine_OpenItemId ON fin.FxRevaluationLine (OpenItemId) WHERE OpenItemId IS NOT NULL;
CREATE INDEX IX_FxRevaluationLine_TreasuryAccountId ON fin.FxRevaluationLine (TreasuryAccountId) WHERE TreasuryAccountId IS NOT NULL;
CREATE INDEX IX_FxRevaluationLine_PartnerId ON fin.FxRevaluationLine (PartnerId) WHERE PartnerId IS NOT NULL;

ALTER TABLE fin.OpenItem ADD CONSTRAINT FK_OpenItem_FxRevaluationRun FOREIGN KEY (LastRevaluationRunId) REFERENCES fin.FxRevaluationRun (Id);

-- Corectiile diferentelor realizate la validarea unei rulari (platile cu data dupa sfarsitul lunii, inregistrate deja) sunt legate de rulare,
-- ca stornarea ei sa le inverseze.
ALTER TABLE fin.FxDifference ADD RevaluationRunId int NULL CONSTRAINT FK_FxDifference_FxRevaluationRun REFERENCES fin.FxRevaluationRun (Id);

CREATE TYPE fin.FxRateList AS TABLE (
    CurrencyCode  char(3)        NOT NULL PRIMARY KEY,
    Rate          decimal(19,6)  NOT NULL,
    Multiplier    int            NOT NULL,
    RateDate      date           NOT NULL,
    Source        varchar(10)    NOT NULL
);
