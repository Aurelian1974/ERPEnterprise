-- 02.02 Contabilitate, C1.3: regulile de mapare din planul romanesc in planurile alternative (C1 3.5) si lunile inghetate (3.6).
-- Regula: prioritate unica in plan (prima potrivita castiga), sursa (un cont cu descendentii lui, interval de coduri normalizate sau lista
-- de conturi), conditii optionale pe linie (firma, debit/credit, tipul, grupa sau partenerul, o dimensiune, grupa de articole, moneda) sau
-- semnul soldului (evaluat in rapoarte), vigoare; tintele cu procente care fac 100. Rezultatul pe liniile de nota vine cu C2 (C1b).

CREATE TABLE fin.AccountMappingRule (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountMappingRule PRIMARY KEY,
    TargetChartId      int               NOT NULL CONSTRAINT FK_AccountMappingRule_Chart REFERENCES fin.ChartOfAccounts (Id),
    Priority           int               NOT NULL,
    SourceKind         tinyint           NOT NULL,
    SourceAccountId    int               NULL CONSTRAINT FK_AccountMappingRule_SourceAccount REFERENCES fin.GlAccount (Id),
    SourceFrom         nvarchar(20)      NULL,
    SourceTo           nvarchar(20)      NULL,
    CompanyId          int               NULL CONSTRAINT FK_AccountMappingRule_Company REFERENCES core.Company (Id),
    Side               tinyint           NOT NULL CONSTRAINT DF_AccountMappingRule_Side DEFAULT (0),
    PartnerKind        tinyint           NULL,
    PartnerGroupId     int               NULL CONSTRAINT FK_AccountMappingRule_PartnerGroup REFERENCES core.PartnerGroup (Id),
    PartnerId          int               NULL CONSTRAINT FK_AccountMappingRule_Partner REFERENCES core.Partner (Id),
    DimensionType      varchar(20)       NULL CONSTRAINT FK_AccountMappingRule_DimensionType REFERENCES core.DimensionType (Code),
    DimensionCondition tinyint           NOT NULL CONSTRAINT DF_AccountMappingRule_DimensionCondition DEFAULT (0),
    DimensionValueId   int               NULL CONSTRAINT FK_AccountMappingRule_DimensionValue REFERENCES core.DimensionValue (Id),
    ItemGroupId        int               NULL CONSTRAINT FK_AccountMappingRule_ItemGroup REFERENCES core.ItemGroup (Id),
    CurrencyCode       char(3)           NULL CONSTRAINT FK_AccountMappingRule_Currency REFERENCES core.Currency (Code),
    BalanceSign        tinyint           NOT NULL CONSTRAINT DF_AccountMappingRule_BalanceSign DEFAULT (0),
    ValidFrom          date              NOT NULL,
    ValidTo            date              NULL,
    IsActive           bit               NOT NULL CONSTRAINT DF_AccountMappingRule_IsActive DEFAULT (1),
    Note               nvarchar(500)     NULL,
    CreatedAt          datetime2(3)      NOT NULL CONSTRAINT DF_AccountMappingRule_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy          int               NOT NULL,
    ModifiedAt         datetime2(3)      NULL,
    ModifiedBy         int               NULL,
    RowVersion         rowversion        NOT NULL,
    CONSTRAINT UQ_AccountMappingRule_Chart_Priority UNIQUE (TargetChartId, Priority),
    -- 1 un cont (cu descendentii lui), 2 interval de coduri normalizate, 3 lista de conturi (AccountMappingSource).
    CONSTRAINT CK_AccountMappingRule_Source CHECK ((SourceKind = 1 AND SourceAccountId IS NOT NULL AND SourceFrom IS NULL AND SourceTo IS NULL)
        OR (SourceKind = 2 AND SourceAccountId IS NULL AND SourceFrom IS NOT NULL AND SourceTo IS NOT NULL AND SourceTo >= SourceFrom)
        OR (SourceKind = 3 AND SourceAccountId IS NULL AND SourceFrom IS NULL AND SourceTo IS NULL)),
    CONSTRAINT CK_AccountMappingRule_Side CHECK (Side BETWEEN 0 AND 2),
    CONSTRAINT CK_AccountMappingRule_PartnerKind CHECK (PartnerKind IS NULL OR PartnerKind BETWEEN 1 AND 4),
    -- Conditia pe dimensiune: 0 fara, 1 valoarea data, 2 are orice valoare, 3 nu are valoare.
    CONSTRAINT CK_AccountMappingRule_Dimension CHECK ((DimensionCondition = 0 AND DimensionType IS NULL AND DimensionValueId IS NULL)
        OR (DimensionCondition = 1 AND DimensionType IS NOT NULL AND DimensionValueId IS NOT NULL)
        OR (DimensionCondition IN (2, 3) AND DimensionType IS NOT NULL AND DimensionValueId IS NULL)),
    -- Regula pe semnul soldului nu foloseste conditii de linie (C1 3.5).
    CONSTRAINT CK_AccountMappingRule_BalanceSign CHECK (BalanceSign = 0 OR (BalanceSign IN (1, 2) AND Side = 0 AND PartnerKind IS NULL
        AND PartnerGroupId IS NULL AND PartnerId IS NULL AND DimensionCondition = 0 AND ItemGroupId IS NULL AND CurrencyCode IS NULL)),
    CONSTRAINT CK_AccountMappingRule_Validity CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
CREATE INDEX IX_AccountMappingRule_Chart_Active_ValidFrom ON fin.AccountMappingRule (TargetChartId, IsActive, ValidFrom);
CREATE INDEX IX_AccountMappingRule_SourceAccountId ON fin.AccountMappingRule (SourceAccountId) WHERE SourceAccountId IS NOT NULL;
CREATE INDEX IX_AccountMappingRule_CompanyId ON fin.AccountMappingRule (CompanyId) WHERE CompanyId IS NOT NULL;
CREATE INDEX IX_AccountMappingRule_PartnerGroupId ON fin.AccountMappingRule (PartnerGroupId) WHERE PartnerGroupId IS NOT NULL;
CREATE INDEX IX_AccountMappingRule_PartnerId ON fin.AccountMappingRule (PartnerId) WHERE PartnerId IS NOT NULL;
CREATE INDEX IX_AccountMappingRule_DimensionValueId ON fin.AccountMappingRule (DimensionValueId) WHERE DimensionValueId IS NOT NULL;
CREATE INDEX IX_AccountMappingRule_ItemGroupId ON fin.AccountMappingRule (ItemGroupId) WHERE ItemGroupId IS NOT NULL;

CREATE TABLE fin.AccountMappingSource (
    RuleId     int  NOT NULL CONSTRAINT FK_AccountMappingSource_Rule REFERENCES fin.AccountMappingRule (Id) ON DELETE CASCADE,
    AccountId  int  NOT NULL CONSTRAINT FK_AccountMappingSource_Account REFERENCES fin.GlAccount (Id),
    CONSTRAINT PK_AccountMappingSource PRIMARY KEY (RuleId, AccountId)
);
CREATE INDEX IX_AccountMappingSource_AccountId ON fin.AccountMappingSource (AccountId);

CREATE TABLE fin.AccountMappingTarget (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountMappingTarget PRIMARY KEY,
    RuleId           int               NOT NULL CONSTRAINT FK_AccountMappingTarget_Rule REFERENCES fin.AccountMappingRule (Id) ON DELETE CASCADE,
    TargetAccountId  int               NOT NULL CONSTRAINT FK_AccountMappingTarget_Account REFERENCES fin.GlAccount (Id),
    PercentShare     decimal(9,6)      NOT NULL,
    SortOrder        tinyint           NOT NULL,
    CONSTRAINT UQ_AccountMappingTarget_Rule_Account UNIQUE (RuleId, TargetAccountId),
    CONSTRAINT CK_AccountMappingTarget_Percent CHECK (PercentShare > 0 AND PercentShare <= 100)
);
CREATE INDEX IX_AccountMappingTarget_TargetAccountId ON fin.AccountMappingTarget (TargetAccountId);

CREATE TABLE fin.AltChartPeriodFreeze (
    ChartId      int           NOT NULL CONSTRAINT FK_AltChartPeriodFreeze_Chart REFERENCES fin.ChartOfAccounts (Id),
    CompanyId    int           NOT NULL CONSTRAINT FK_AltChartPeriodFreeze_Company REFERENCES core.Company (Id),
    PeriodYear   smallint      NOT NULL,
    PeriodMonth  tinyint       NOT NULL,
    FrozenAt     datetime2(3)  NOT NULL CONSTRAINT DF_AltChartPeriodFreeze_FrozenAt DEFAULT (sysutcdatetime()),
    FrozenBy     int           NOT NULL,
    CONSTRAINT PK_AltChartPeriodFreeze PRIMARY KEY (ChartId, CompanyId, PeriodYear, PeriodMonth),
    CONSTRAINT CK_AltChartPeriodFreeze_Month CHECK (PeriodMonth BETWEEN 1 AND 12)
);
CREATE INDEX IX_AltChartPeriodFreeze_CompanyId ON fin.AltChartPeriodFreeze (CompanyId);
