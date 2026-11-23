-- Multivaluta, diferentele de curs realizate la alocare (02.02, F4 3.1–3.3, 4.1, 7). Partida primeste valoarea contabila in lei a restului
-- (OpenAmountRon, cu semnul partidei) si cursul ei contabil; alocarea primeste cursul incrucisat cand monedele difera; profilul partenerului,
-- regula de curs pentru plata in alta moneda; fin.FxDifference tine diferentele realizate (pozitiv = castig).
ALTER TABLE fin.OpenItem ADD
    OpenAmountRon        decimal(19,2)  NULL,
    CarryingRate         decimal(19,6)  NULL,
    LastRevaluationRunId int            NULL,
    IsMonetary           bit            NOT NULL CONSTRAINT DF_OpenItem_IsMonetary DEFAULT (1),
    IndexCurrencyCode    char(3)        NULL CONSTRAINT FK_OpenItem_Currency_Index REFERENCES core.Currency (Code),
    IndexedAmount        decimal(19,2)  NULL,
    IndexedOpenAmount    decimal(19,2)  NULL;

ALTER TABLE fin.Allocation ADD
    CrossRate        decimal(19,6)  NULL,
    CrossRateSource  tinyint        NULL;

ALTER TABLE fin.PartnerFinanceProfile ADD
    CrossCurrencyRule           tinyint       NOT NULL CONSTRAINT DF_PartnerFinanceProfile_CrossCurrencyRule DEFAULT (1),
    CrossCurrencyMarkupPercent  decimal(9,4)  NOT NULL CONSTRAINT DF_PartnerFinanceProfile_CrossCurrencyMarkupPercent DEFAULT (0);
GO

-- Valoarea contabila a restului: valoarea in lei a documentului minus valorile in lei ale alocarilor active (fiecare pe partea ei). Facturile
-- de avans nu sunt elemente monetare (F4 2, regula 1).
UPDATE o
SET OpenAmountRon = CASE WHEN o.OpenAmount = 0 THEN 0 ELSE o.AmountRon - ISNULL(d.Ron, 0) + ISNULL(c.Ron, 0) END,
    CarryingRate = o.ExchangeRate,
    IsMonetary = CASE WHEN o.Kind = 3 THEN 0 ELSE 1 END
FROM fin.OpenItem AS o
OUTER APPLY (SELECT SUM(a.AmountRonDebit) AS Ron FROM fin.Allocation AS a WHERE a.DebitOpenItemId = o.Id AND a.Status = 1) AS d
OUTER APPLY (SELECT SUM(a.AmountRonCredit) AS Ron FROM fin.Allocation AS a WHERE a.CreditOpenItemId = o.Id AND a.Status = 1) AS c;

-- Restul in lei al unui rest diferit de zero are semnul lui (o rotunjire veche il poate face zero); un rest zero are valoarea zero.
UPDATE fin.OpenItem SET OpenAmountRon = 0 WHERE (OpenAmount > 0 AND OpenAmountRon < 0) OR (OpenAmount < 0 AND OpenAmountRon > 0);
GO

ALTER TABLE fin.OpenItem ALTER COLUMN OpenAmountRon decimal(19,2) NOT NULL;
ALTER TABLE fin.OpenItem ALTER COLUMN CarryingRate decimal(19,6) NOT NULL;
ALTER TABLE fin.OpenItem ADD
    CONSTRAINT CK_OpenItem_OpenAmountRon CHECK ((OpenAmount = 0 AND OpenAmountRon = 0) OR (OpenAmount > 0 AND OpenAmountRon >= 0)
        OR (OpenAmount < 0 AND OpenAmountRon <= 0)),
    CONSTRAINT CK_OpenItem_Indexed CHECK ((IndexCurrencyCode IS NULL AND IndexedAmount IS NULL AND IndexedOpenAmount IS NULL)
        OR (IndexCurrencyCode IS NOT NULL AND IndexedAmount IS NOT NULL AND IndexedOpenAmount IS NOT NULL));
ALTER TABLE fin.Allocation ADD CONSTRAINT CK_Allocation_CrossRate CHECK ((CrossRate IS NULL AND CrossRateSource IS NULL)
    OR (CrossRate > 0 AND CrossRateSource BETWEEN 1 AND 4));
ALTER TABLE fin.PartnerFinanceProfile ADD CONSTRAINT CK_PartnerFinanceProfile_CrossCurrency CHECK (CrossCurrencyRule BETWEEN 1 AND 4
    AND CrossCurrencyMarkupPercent >= 0 AND CrossCurrencyMarkupPercent < 100);

-- Diferenta de curs realizata (F4 3.2). Source: 1 Alocare, 2 Schimb valutar, 3 Iesire din cont in valuta, 4 Indexare la plata.
CREATE TABLE fin.FxDifference (
    Id                       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_FxDifference PRIMARY KEY,
    CompanyId                int            NOT NULL CONSTRAINT FK_FxDifference_Company REFERENCES core.Company (Id),
    DifferenceDate           date           NOT NULL,
    Source                   tinyint        NOT NULL,
    AllocationId             bigint         NULL CONSTRAINT FK_FxDifference_Allocation REFERENCES fin.Allocation (Id),
    BankStatementLinePartId  bigint         NULL,
    PartnerId                int            NULL CONSTRAINT FK_FxDifference_Partner REFERENCES core.Partner (Id),
    Role                     tinyint        NULL,
    OpenItemId               bigint         NULL CONSTRAINT FK_FxDifference_OpenItem REFERENCES fin.OpenItem (Id),
    CurrencyCode             char(3)        NOT NULL CONSTRAINT FK_FxDifference_Currency REFERENCES core.Currency (Code),
    ForeignAmount            decimal(19,2)  NOT NULL,
    CarryingAmountRon        decimal(19,2)  NOT NULL,
    SettlementAmountRon      decimal(19,2)  NOT NULL,
    DifferenceRon            decimal(19,2)  NOT NULL,
    IsIndexed                bit            NOT NULL CONSTRAINT DF_FxDifference_IsIndexed DEFAULT (0),
    GlAccountCode            nvarchar(20)   NOT NULL,
    IsReversal               bit            NOT NULL CONSTRAINT DF_FxDifference_IsReversal DEFAULT (0),
    ReversalOfId             bigint         NULL CONSTRAINT FK_FxDifference_FxDifference_ReversalOf REFERENCES fin.FxDifference (Id),
    CreatedAt                datetime2(3)   NOT NULL CONSTRAINT DF_FxDifference_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy                int            NOT NULL,
    CONSTRAINT CK_FxDifference_Source CHECK (Source BETWEEN 1 AND 4),
    CONSTRAINT CK_FxDifference_NotZero CHECK (DifferenceRon <> 0)
);
CREATE INDEX IX_FxDifference_CompanyId_DifferenceDate ON fin.FxDifference (CompanyId, DifferenceDate);
CREATE INDEX IX_FxDifference_AllocationId ON fin.FxDifference (AllocationId) WHERE AllocationId IS NOT NULL;

-- Lotul de alocari primeste sumele pe fiecare parte (monede diferite), valorile in lei calculate de FxCalculator, cursul incrucisat si
-- diferenta realizata (cu partida in valuta a carei valoare s-a realizat).
DROP PROCEDURE IF EXISTS fin.usp_Allocation_CreateBatch;
DROP TYPE fin.AllocationList;
CREATE TYPE fin.AllocationList AS TABLE (
    RowNo             int            NOT NULL PRIMARY KEY,
    DebitOpenItemId   bigint         NOT NULL,
    CreditOpenItemId  bigint         NOT NULL,
    DebitAmount       decimal(19,2)  NOT NULL,
    CreditAmount      decimal(19,2)  NOT NULL,
    AmountRonDebit    decimal(19,2)  NOT NULL,
    AmountRonCredit   decimal(19,2)  NOT NULL,
    AllocationDate    date           NOT NULL,
    Source            tinyint        NOT NULL,
    CompensationId    int            NULL,
    WriteOffId        int            NULL,
    CommercialPaperId int            NULL,
    CrossRate         decimal(19,6)  NULL,
    CrossRateSource   tinyint        NULL,
    DifferenceRon     decimal(19,2)  NOT NULL,
    FxOpenItemId      bigint         NULL
);
