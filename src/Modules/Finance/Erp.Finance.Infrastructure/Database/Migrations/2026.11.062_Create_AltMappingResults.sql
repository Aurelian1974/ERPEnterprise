-- 02.02 Contabilitate, C1b: rezultatul maparii liniilor de nota in planurile alternative (C1 3.6) si jurnalul recalcularilor (C1 4.4).
-- fin.JournalLineAltAccount e rezultat de calcul, sters si rescris la recalculare: fara ModifiedAt / RowVersion (abaterea din C1 7). Firma,
-- anul, luna si partea se repeta din linie, ca balanta alternativa sa adune fara sa citeasca liniile.
CREATE TABLE fin.JournalLineAltAccount (
    Id               bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_JournalLineAltAccount PRIMARY KEY,
    JournalLineId    bigint         NOT NULL CONSTRAINT FK_JournalLineAltAccount_Line REFERENCES fin.JournalLine (Id) ON DELETE CASCADE,
    ChartId          int            NOT NULL CONSTRAINT FK_JournalLineAltAccount_Chart REFERENCES fin.ChartOfAccounts (Id),
    CompanyId        int            NOT NULL,
    PeriodYear       smallint       NOT NULL,
    PeriodMonth      tinyint        NOT NULL,
    Side             tinyint        NOT NULL,
    TargetAccountId  int            NULL CONSTRAINT FK_JournalLineAltAccount_Target REFERENCES fin.GlAccount (Id),
    RuleId           int            NULL CONSTRAINT FK_JournalLineAltAccount_Rule REFERENCES fin.AccountMappingRule (Id),
    PercentShare     decimal(9,6)   NULL,
    AmountRon        decimal(19,2)  NOT NULL,
    MappedAt         datetime2(3)   NOT NULL CONSTRAINT DF_JournalLineAltAccount_MappedAt DEFAULT (sysutcdatetime())
);
CREATE INDEX IX_JournalLineAltAccount_Chart_Line ON fin.JournalLineAltAccount (ChartId, JournalLineId);
CREATE INDEX IX_JournalLineAltAccount_JournalLineId ON fin.JournalLineAltAccount (JournalLineId);
CREATE INDEX IX_JournalLineAltAccount_Period ON fin.JournalLineAltAccount (ChartId, CompanyId, PeriodYear, PeriodMonth) INCLUDE (TargetAccountId, Side, AmountRon);
CREATE INDEX IX_JournalLineAltAccount_Unmapped ON fin.JournalLineAltAccount (ChartId) INCLUDE (CompanyId, PeriodYear, PeriodMonth) WHERE TargetAccountId IS NULL;
CREATE INDEX IX_JournalLineAltAccount_TargetAccountId ON fin.JournalLineAltAccount (TargetAccountId) WHERE TargetAccountId IS NOT NULL;
CREATE INDEX IX_JournalLineAltAccount_RuleId ON fin.JournalLineAltAccount (RuleId) WHERE RuleId IS NOT NULL;

CREATE TABLE fin.AltMappingRun (
    Id            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AltMappingRun PRIMARY KEY,
    ChartId       int            NOT NULL CONSTRAINT FK_AltMappingRun_Chart REFERENCES fin.ChartOfAccounts (Id),
    CompanyId     int            NOT NULL CONSTRAINT FK_AltMappingRun_Company REFERENCES core.Company (Id),
    PeriodYear    smallint       NOT NULL,
    PeriodMonth   tinyint        NOT NULL,
    LineCount     int            NOT NULL,
    UnmappedCount int            NOT NULL,
    DurationMs    int            NOT NULL,
    StartedAt     datetime2(3)   NOT NULL,
    StartedBy     int            NOT NULL
);
CREATE INDEX IX_AltMappingRun_Chart_StartedAt ON fin.AltMappingRun (ChartId, StartedAt);
CREATE INDEX IX_AltMappingRun_CompanyId ON fin.AltMappingRun (CompanyId);
