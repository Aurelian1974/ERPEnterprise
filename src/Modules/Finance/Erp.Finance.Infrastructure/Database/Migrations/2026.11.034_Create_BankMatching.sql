-- Potrivirea automata a liniilor de extras (02.02, F2 3.5, 3.7, 4.3, 4.5, 4.6): regulile clientului, propunerile cu scor, procesatorii de
-- carduri, decontarile de card asteptate, perechea unei parti de virament intern si sumele in tranzit.

-- Regula de potrivire (F2 3.5): conditiile (toate cele completate) si actiunea; textul se compara fara majuscule si diacritice.
-- Direction: 1 Intrare, 2 Iesire, NULL oricare.
CREATE TABLE fin.MatchingRule (
    Id                      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MatchingRule PRIMARY KEY,
    CompanyId               int               NOT NULL CONSTRAINT FK_MatchingRule_Company REFERENCES core.Company (Id),
    Priority                int               NOT NULL,
    Name                    nvarchar(200)     NOT NULL,
    TreasuryAccountId       int               NULL CONSTRAINT FK_MatchingRule_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    Direction               tinyint           NULL,
    CounterpartyIban        varchar(34)       NULL,
    CounterpartyFiscalCode  varchar(20)       NULL,
    RemittanceContains      nvarchar(200)     NULL,
    BankTransactionCode     varchar(35)       NULL,
    AmountFrom              decimal(19,2)     NULL,
    AmountTo                decimal(19,2)     NULL,
    TreasuryOperationTypeId int               NOT NULL CONSTRAINT FK_MatchingRule_TreasuryOperationType REFERENCES fin.TreasuryOperationType (Id),
    PartnerId               int               NULL CONSTRAINT FK_MatchingRule_Partner REFERENCES core.Partner (Id),
    CostCenterId            int               NULL CONSTRAINT FK_MatchingRule_CostCenter REFERENCES core.DimensionValue (Id),
    ProjectId               int               NULL CONSTRAINT FK_MatchingRule_Project REFERENCES core.DimensionValue (Id),
    NoteTemplate            nvarchar(500)     NULL,
    AutoValidate            bit               NOT NULL CONSTRAINT DF_MatchingRule_AutoValidate DEFAULT (0),
    CreatedFromLineId       bigint            NULL CONSTRAINT FK_MatchingRule_CreatedFromLine REFERENCES fin.BankStatementLine (Id),
    IsActive                bit               NOT NULL CONSTRAINT DF_MatchingRule_IsActive DEFAULT (1),
    CreatedAt               datetime2(3)      NOT NULL CONSTRAINT DF_MatchingRule_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy               int               NOT NULL,
    ModifiedAt              datetime2(3)      NULL,
    ModifiedBy              int               NULL,
    RowVersion              rowversion        NOT NULL,
    RemittanceSearch        AS (RemittanceContains COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED,
    CONSTRAINT CK_MatchingRule_Direction CHECK (Direction IS NULL OR Direction IN (1, 2)),
    CONSTRAINT CK_MatchingRule_Amounts CHECK (AmountFrom IS NULL OR AmountTo IS NULL OR AmountFrom <= AmountTo),
    CONSTRAINT CK_MatchingRule_HasCondition CHECK (CounterpartyIban IS NOT NULL OR CounterpartyFiscalCode IS NOT NULL OR RemittanceContains IS NOT NULL
        OR BankTransactionCode IS NOT NULL)
);
GO

CREATE INDEX IX_MatchingRule_CompanyId_IsActive_Priority ON fin.MatchingRule (CompanyId, IsActive, Priority);
GO

-- Propunerile motorului pentru o linie (F2 4.3 pasul 6), cu scor; se sterg la explicarea sau validarea liniei.
CREATE TABLE fin.MatchSuggestion (
    Id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_MatchSuggestion PRIMARY KEY,
    BankStatementLineId bigint            NOT NULL,
    Kind                tinyint           NOT NULL,
    PaymentOrderId      int               NULL CONSTRAINT FK_MatchSuggestion_PaymentOrder REFERENCES fin.PaymentOrder (Id),
    PartnerId           int               NULL CONSTRAINT FK_MatchSuggestion_Partner REFERENCES core.Partner (Id),
    Amount              decimal(19,2)     NOT NULL,
    Score               tinyint           NOT NULL,
    Description         nvarchar(300)     NOT NULL,
    CreatedAt           datetime2(3)      NOT NULL CONSTRAINT DF_MatchSuggestion_CreatedAt DEFAULT (sysutcdatetime()),
    CONSTRAINT FK_MatchSuggestion_BankStatementLine FOREIGN KEY (BankStatementLineId) REFERENCES fin.BankStatementLine (Id) ON DELETE CASCADE,
    CONSTRAINT CK_MatchSuggestion_Kind CHECK (Kind BETWEEN 1 AND 3),
    CONSTRAINT CK_MatchSuggestion_Score CHECK (Score BETWEEN 0 AND 100)
);
GO

CREATE INDEX IX_MatchSuggestion_BankStatementLineId ON fin.MatchSuggestion (BankStatementLineId);
GO

-- Procesatorul de carduri al firmei (F2 3.7): codul folosit de magazine (02.10) si IBAN-ul de pe care vin decontarile.
CREATE TABLE fin.CardAcquirer (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CardAcquirer PRIMARY KEY,
    CompanyId        int               NOT NULL CONSTRAINT FK_CardAcquirer_Company REFERENCES core.Company (Id),
    Code             nvarchar(30)      NOT NULL,
    Name             nvarchar(200)     NOT NULL,
    CounterpartyIban varchar(34)       NULL,
    IsActive         bit               NOT NULL CONSTRAINT DF_CardAcquirer_IsActive DEFAULT (1),
    CreatedAt        datetime2(3)      NOT NULL CONSTRAINT DF_CardAcquirer_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int               NOT NULL,
    ModifiedAt       datetime2(3)      NULL,
    ModifiedBy       int               NULL,
    RowVersion       rowversion        NOT NULL,
    CodeKey          AS UPPER(Code) PERSISTED
);
GO

CREATE UNIQUE INDEX UX_CardAcquirer_CompanyId_CodeKey ON fin.CardAcquirer (CompanyId, CodeKey);
GO

-- Decontarea de card asteptata (F2 3.7). Status: 1 In asteptare, 2 Decontata, 3 Decontata cu diferenta (cere explicatie).
CREATE TABLE fin.CardSettlementExpected (
    Id                     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CardSettlementExpected PRIMARY KEY,
    CompanyId              int               NOT NULL CONSTRAINT FK_CardSettlementExpected_Company REFERENCES core.Company (Id),
    AcquirerCode           nvarchar(30)      NOT NULL,
    SalesDate              date              NOT NULL,
    GrossAmount            decimal(19,2)     NOT NULL,
    ExpectedSettlementDate date              NOT NULL,
    SourceModule           varchar(30)       NOT NULL,
    SourceRef              nvarchar(100)     NOT NULL,
    SettledAmount          decimal(19,2)     NULL,
    FeeAmount              decimal(19,2)     NULL,
    Status                 tinyint           NOT NULL CONSTRAINT DF_CardSettlementExpected_Status DEFAULT (1),
    BankStatementLineId    bigint            NULL CONSTRAINT FK_CardSettlementExpected_BankStatementLine REFERENCES fin.BankStatementLine (Id),
    Note                   nvarchar(500)     NULL,
    CreatedAt              datetime2(3)      NOT NULL CONSTRAINT DF_CardSettlementExpected_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy              int               NOT NULL,
    ModifiedAt             datetime2(3)      NULL,
    ModifiedBy             int               NULL,
    RowVersion             rowversion        NOT NULL,
    AcquirerKey            AS UPPER(AcquirerCode) PERSISTED,
    CONSTRAINT UQ_CardSettlementExpected_Source UNIQUE (SourceModule, SourceRef),
    CONSTRAINT CK_CardSettlementExpected_Status CHECK (Status BETWEEN 1 AND 3),
    CONSTRAINT CK_CardSettlementExpected_Gross CHECK (GrossAmount > 0)
);
GO

CREATE INDEX IX_CardSettlementExpected_Company_Acquirer_Status_Date ON fin.CardSettlementExpected (CompanyId, AcquirerKey, Status, ExpectedSettlementDate);
GO

ALTER TABLE fin.BankStatementLinePart ADD CONSTRAINT FK_BankStatementLinePart_CardSettlementExpected
    FOREIGN KEY (CardSettlementExpectedId) REFERENCES fin.CardSettlementExpected (Id);
ALTER TABLE fin.BankStatementLinePart ADD PairedBankStatementLineId bigint NULL
    CONSTRAINT FK_BankStatementLinePart_PairedLine REFERENCES fin.BankStatementLine (Id);
GO

CREATE INDEX IX_BankStatementLinePart_CounterTreasuryAccountId ON fin.BankStatementLinePart (CounterTreasuryAccountId) WHERE CounterTreasuryAccountId IS NOT NULL;
CREATE INDEX IX_BankStatementLinePart_PairedBankStatementLineId ON fin.BankStatementLinePart (PairedBankStatementLineId)
    WHERE PairedBankStatementLineId IS NOT NULL;
GO

-- Sumele in tranzit (F2 3.7): depunerile si ridicarile de numerar validate in casa fara pereche in extras, viramentele interne explicate pe
-- o singura parte si decontarile de card in asteptare, cu data de la care se numara vechimea. Kind: 1 Numerar, 2 Virament intern, 3 Card.
CREATE VIEW fin.v_TransitItem
AS
SELECT 1 AS Kind, d.CompanyId, dl.CounterTreasuryAccountId AS TreasuryAccountId, d.TreasuryAccountId AS SourceAccountId, d.DocumentDate AS ItemDate,
    CASE WHEN t.Code = N'PLATA-DEPUNERE-BANCA' THEN dl.Amount ELSE -dl.Amount END AS Amount, d.Number AS Reference, CAST(d.Id AS bigint) AS SourceId
FROM fin.CashDocument AS d
JOIN fin.CashDocumentLine AS dl ON dl.CashDocumentId = d.Id
JOIN fin.TreasuryOperationType AS t ON t.Id = dl.TreasuryOperationTypeId AND t.Code IN (N'PLATA-DEPUNERE-BANCA', N'INC-RIDICARE-BANCA')
WHERE d.Status = 2 AND d.ReversalOfId IS NULL
  AND NOT EXISTS (SELECT 1 FROM fin.CashDocument AS r WHERE r.ReversalOfId = d.Id)
  AND NOT EXISTS (SELECT 1 FROM fin.BankStatementLinePart AS p WHERE p.CashDocumentId = d.Id)
UNION ALL
SELECT 2, s.CompanyId, p.CounterTreasuryAccountId, l.TreasuryAccountId, l.BookingDate, -p.Amount, s.StatementNo, l.Id
FROM fin.BankStatementLinePart AS p
JOIN fin.BankStatementLine AS l ON l.Id = p.BankStatementLineId AND l.MatchStatus IN (3, 4)
JOIN fin.BankStatement AS s ON s.Id = l.BankStatementId
JOIN fin.TreasuryOperationType AS t ON t.Id = p.TreasuryOperationTypeId AND t.Code IN (N'BANK-TRANSFER-INTERN', N'BANK-SCHIMB-VALUTAR')
WHERE p.PairedBankStatementLineId IS NULL
  AND NOT EXISTS (SELECT 1 FROM fin.BankStatementLinePart AS q WHERE q.PairedBankStatementLineId = l.Id)
UNION ALL
SELECT 3, c.CompanyId, NULL, NULL, c.SalesDate, c.GrossAmount, c.SourceRef, c.Id
FROM fin.CardSettlementExpected AS c
WHERE c.Status = 1 AND NOT EXISTS (SELECT 1 FROM fin.BankStatementLinePart AS p WHERE p.CardSettlementExpectedId = c.Id);
GO

-- Partile unei linii pot explica o decontare de card asteptata (CardSettlementExpectedId): tipul tabel se reface; procedura care il
-- foloseste se recreeaza din Programmability dupa migrari.
DROP PROCEDURE IF EXISTS fin.usp_BankStatementLine_Explain;
DROP TYPE fin.BankStatementLinePartList;
GO

CREATE TYPE fin.BankStatementLinePartList AS TABLE (
    PartNumber               smallint      NOT NULL PRIMARY KEY,
    TreasuryOperationTypeId  int           NOT NULL,
    Amount                   decimal(19,2) NOT NULL,
    PartnerId                int           NULL,
    EmployeeId               int           NULL,
    SettlementRef            nvarchar(100) NULL,
    PaymentOrderId           int           NULL,
    CashDocumentId           int           NULL,
    CounterTreasuryAccountId int           NULL,
    CardSettlementExpectedId int           NULL,
    BudgetObligationRef      nvarchar(50)  NULL,
    CostCenterId             int           NULL,
    ProjectId                int           NULL,
    Note                     nvarchar(500) NULL
);
GO
