-- Fisa de calcul a impozitului pe profit (02.02, C4 3.6, 4.4): pe firma, an si trimestru (cumulat de la inceputul anului; trimestrul 4 e
-- calculul anual, pentru D101), regimul (1 profit, 2 microintreprindere), cota, impozitul trimestrului si nota 691 = 4411; randurile fisei,
-- calculate sau manuale cu motiv (decizia 6).
CREATE TABLE fin.ProfitTaxComputation (
    Id            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProfitTaxComputation PRIMARY KEY,
    CompanyId     int               NOT NULL CONSTRAINT FK_ProfitTaxComputation_Company REFERENCES core.Company (Id),
    Year          smallint          NOT NULL,
    Quarter       tinyint           NOT NULL,
    Regime        tinyint           NOT NULL,
    TaxRate       decimal(5,2)      NOT NULL,
    TaxDue        decimal(19,2)     NOT NULL,
    EntryId       bigint            NULL CONSTRAINT FK_ProfitTaxComputation_Entry REFERENCES fin.JournalEntry (Id),
    PostedAmount  decimal(19,2)     NULL,
    CalculatedAt  datetime2(3)      NOT NULL,
    CalculatedBy  int               NOT NULL,
    PostedAt      datetime2(3)      NULL,
    PostedBy      int               NULL,
    RowVersion    rowversion        NOT NULL,
    CONSTRAINT CK_ProfitTaxComputation_Quarter CHECK (Quarter BETWEEN 1 AND 4),
    CONSTRAINT CK_ProfitTaxComputation_Regime CHECK (Regime IN (1, 2))
);

CREATE UNIQUE INDEX UX_ProfitTaxComputation_Company_Year_Quarter ON fin.ProfitTaxComputation (CompanyId, Year, Quarter);

CREATE TABLE fin.ProfitTaxComputationLine (
    ComputationId  int               NOT NULL CONSTRAINT FK_ProfitTaxComputationLine_Computation REFERENCES fin.ProfitTaxComputation (Id),
    RowCode        varchar(30)       NOT NULL,
    Label          nvarchar(200)     NOT NULL,
    SortOrder      int               NOT NULL,
    IsManual       bit               NOT NULL,
    Amount         decimal(19,2)     NOT NULL,
    Reason         nvarchar(300)     NULL,
    CONSTRAINT PK_ProfitTaxComputationLine PRIMARY KEY (ComputationId, RowCode)
);
