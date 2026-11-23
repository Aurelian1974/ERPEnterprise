-- Efectele comerciale (02.02, F3 3.5, 4.5; Legea 59/1934 CEC, Legea 58/1934 bilet la ordin). Direction: 1 Primit de la client, 2 Emis catre
-- furnizor. Kind: 1 CEC, 2 Bilet la ordin. Status: 1 In portofoliu (emis, la cele emise), 2 Depus la banca, 3 Incasat / platit, 4 Refuzat,
-- 5 Girat (transmis altui furnizor), 6 Stornat. OpenItemId: partida efectului (tip 8), care stinge facturile la primire (Source = 5).
CREATE TABLE fin.CommercialPaper (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CommercialPaper PRIMARY KEY,
    CompanyId          int               NOT NULL CONSTRAINT FK_CommercialPaper_Company REFERENCES core.Company (Id),
    PartnerId          int               NOT NULL CONSTRAINT FK_CommercialPaper_Partner REFERENCES core.Partner (Id),
    Direction          tinyint           NOT NULL,
    Kind               tinyint           NOT NULL,
    Series             nvarchar(20)      NOT NULL,
    Number             nvarchar(30)      NOT NULL,
    IssuerBank         nvarchar(100)     NULL,
    IssuerAccount      varchar(34)       NULL,
    Amount             decimal(19,2)     NOT NULL,
    CurrencyCode       char(3)           NOT NULL CONSTRAINT FK_CommercialPaper_Currency REFERENCES core.Currency (Code),
    IssueDate          date              NOT NULL,
    MaturityDate       date              NOT NULL,
    TreasuryAccountId  int               NULL CONSTRAINT FK_CommercialPaper_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    Status             tinyint           NOT NULL CONSTRAINT DF_CommercialPaper_Status DEFAULT (1),
    GlAccountCode      nvarchar(20)      NULL,
    OpenItemId         bigint            NULL CONSTRAINT FK_CommercialPaper_OpenItem REFERENCES fin.OpenItem (Id),
    EndorsedToPartnerId int              NULL CONSTRAINT FK_CommercialPaper_Partner_Endorsed REFERENCES core.Partner (Id),
    Note               nvarchar(500)     NULL,
    CreatedAt          datetime2(3)      NOT NULL CONSTRAINT DF_CommercialPaper_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy          int               NOT NULL,
    ModifiedAt         datetime2(3)      NULL,
    ModifiedBy         int               NULL,
    RowVersion         rowversion        NOT NULL,
    CONSTRAINT UQ_CommercialPaper_Instrument UNIQUE (CompanyId, PartnerId, Kind, Series, Number),
    CONSTRAINT CK_CommercialPaper_Direction CHECK (Direction IN (1, 2)),
    CONSTRAINT CK_CommercialPaper_Kind CHECK (Kind IN (1, 2)),
    CONSTRAINT CK_CommercialPaper_Status CHECK (Status BETWEEN 1 AND 6),
    CONSTRAINT CK_CommercialPaper_Amount CHECK (Amount > 0)
);
CREATE INDEX IX_CommercialPaper_CompanyId_Status_MaturityDate ON fin.CommercialPaper (CompanyId, Status, MaturityDate);

-- Istoricul starilor (F3 3.5).
CREATE TABLE fin.CommercialPaperEvent (
    Id                   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CommercialPaperEvent PRIMARY KEY,
    CommercialPaperId    int            NOT NULL CONSTRAINT FK_CommercialPaperEvent_CommercialPaper REFERENCES fin.CommercialPaper (Id),
    Status               tinyint        NOT NULL,
    EventDate            date           NOT NULL,
    TreasuryAccountId    int            NULL CONSTRAINT FK_CommercialPaperEvent_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    BankStatementLineId  bigint         NULL CONSTRAINT FK_CommercialPaperEvent_BankStatementLine REFERENCES fin.BankStatementLine (Id),
    Note                 nvarchar(500)  NULL,
    CreatedAt            datetime2(3)   NOT NULL CONSTRAINT DF_CommercialPaperEvent_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy            int            NOT NULL
);
CREATE INDEX IX_CommercialPaperEvent_CommercialPaperId ON fin.CommercialPaperEvent (CommercialPaperId);

ALTER TABLE fin.Allocation ADD CONSTRAINT FK_Allocation_CommercialPaper FOREIGN KEY (CommercialPaperId) REFERENCES fin.CommercialPaper (Id);
