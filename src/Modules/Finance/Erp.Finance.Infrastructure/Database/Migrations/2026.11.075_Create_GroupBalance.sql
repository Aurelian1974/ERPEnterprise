-- Balanta de grup (02.02, C5 3.7, decizia 3): conturile alternative marcate pentru eliminarea intra-grup, perechile de eliminare (creanta ↔
-- datorie, cheltuiala ↔ venit) si rularile salvate (coloanele pe firme si eliminarile), ca rezultatul raportat sa poata fi refacut exact.
ALTER TABLE fin.GlAccount ADD EliminateIntercompany bit NOT NULL CONSTRAINT DF_GlAccount_EliminateIntercompany DEFAULT (0);
GO

CREATE TABLE fin.IntercompanyPair (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_IntercompanyPair PRIMARY KEY,
    ChartId          int               NOT NULL CONSTRAINT FK_IntercompanyPair_Chart REFERENCES fin.ChartOfAccounts (Id),
    Name             nvarchar(100)     NOT NULL,
    FirstAccountId   int               NOT NULL CONSTRAINT FK_IntercompanyPair_First REFERENCES fin.GlAccount (Id),
    SecondAccountId  int               NOT NULL CONSTRAINT FK_IntercompanyPair_Second REFERENCES fin.GlAccount (Id),
    CreatedAt        datetime2(3)      NOT NULL CONSTRAINT DF_IntercompanyPair_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int               NOT NULL
);

CREATE UNIQUE INDEX UX_IntercompanyPair_Chart_Accounts ON fin.IntercompanyPair (ChartId, FirstAccountId, SecondAccountId);

CREATE TABLE fin.GroupBalanceRun (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_GroupBalanceRun PRIMARY KEY,
    ChartId      int               NOT NULL CONSTRAINT FK_GroupBalanceRun_Chart REFERENCES fin.ChartOfAccounts (Id),
    PeriodYear   smallint          NOT NULL,
    PeriodMonth  tinyint           NOT NULL,
    IsFinal      bit               NOT NULL,
    RunAt        datetime2(3)      NOT NULL CONSTRAINT DF_GroupBalanceRun_RunAt DEFAULT (sysutcdatetime()),
    RunBy        int               NOT NULL
);

CREATE INDEX IX_GroupBalanceRun_Chart_Period ON fin.GroupBalanceRun (ChartId, PeriodYear, PeriodMonth);

CREATE TABLE fin.GroupBalanceRunLine (
    Id         bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_GroupBalanceRunLine PRIMARY KEY,
    RunId      int               NOT NULL CONSTRAINT FK_GroupBalanceRunLine_Run REFERENCES fin.GroupBalanceRun (Id),
    CompanyId  int               NOT NULL,
    Kind       tinyint           NOT NULL,
    AccountId  int               NOT NULL,
    DebitRon   decimal(19,2)     NOT NULL,
    CreditRon  decimal(19,2)     NOT NULL,
    -- Kind: 1 soldul firmei (cumulat de la inceputul anului), 2 eliminarea intra-grup (inversul liniilor cu partener din grup).
    CONSTRAINT CK_GroupBalanceRunLine_Kind CHECK (Kind IN (1, 2))
);

CREATE INDEX IX_GroupBalanceRunLine_Run ON fin.GroupBalanceRunLine (RunId);
