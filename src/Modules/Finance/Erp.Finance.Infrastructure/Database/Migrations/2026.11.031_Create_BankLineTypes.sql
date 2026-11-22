-- Tipurile tabel pentru explicarea si validarea liniilor de extras (02.02, F2 3.4 si 4.4; SP-07, SP-08).

-- Partile unei linii de extras, inlocuite integral la explicare (fin.usp_BankStatementLine_Explain).
CREATE TYPE fin.BankStatementLinePartList AS TABLE (
    PartNumber               smallint      NOT NULL PRIMARY KEY,
    TreasuryOperationTypeId  int           NOT NULL,
    Amount                   decimal(19,2) NOT NULL,
    PartnerId                int           NULL,
    EmployeeId               int           NULL,
    SettlementRef            nvarchar(100) NULL,
    CashDocumentId           int           NULL,
    CounterTreasuryAccountId int           NULL,
    BudgetObligationRef      nvarchar(50)  NULL,
    CostCenterId             int           NULL,
    ProjectId                int           NULL,
    Note                     nvarchar(500) NULL
);
GO

-- Liniile de validat sau de citit, cu versiunea vazuta de utilizator (SP-18).
CREATE TYPE fin.BankLineVersionList AS TABLE (
    Id         bigint    NOT NULL PRIMARY KEY,
    RowVersion binary(8) NULL
);
GO
