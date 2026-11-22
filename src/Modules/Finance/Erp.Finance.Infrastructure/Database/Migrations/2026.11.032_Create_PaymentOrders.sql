-- Ordinele de plata (02.02, F2 3.6): antetul (un beneficiar, un cont sursa), liniile (ce stinge plata) si setarile Finantelor pe firma.
-- Status: 1 Ciorna, 2 Validat, 3 Stornat. ConfirmationStatus (doar la validate): 1 Neconfirmat, 2 Confirmat, 3 Respins. Origin: 1 ecranul
-- Ordine de plata, 2 ecranul Extras de cont, 3 propunere F5, 4 alt modul. Purpose: 1 Furnizori, 2 Salarii, 3 Buget de stat, 4 Diverse.
-- Storno-ul e un OP validat cu sume negative (ReversalOfId), fara confirmare pe extras.

CREATE TABLE fin.PaymentOrder (
    Id                     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentOrder PRIMARY KEY,
    CompanyId              int               NOT NULL CONSTRAINT FK_PaymentOrder_Company REFERENCES core.Company (Id),
    TreasuryAccountId      int               NOT NULL CONSTRAINT FK_PaymentOrder_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    Number                 nvarchar(30)      NULL,
    DocumentDate           date              NOT NULL,
    RequestedExecutionDate date              NOT NULL,
    IsUrgent               bit               NOT NULL CONSTRAINT DF_PaymentOrder_IsUrgent DEFAULT (0),
    Origin                 tinyint           NOT NULL,
    Purpose                tinyint           NOT NULL,
    PartnerId              int               NULL CONSTRAINT FK_PaymentOrder_Partner REFERENCES core.Partner (Id),
    EmployeeId             int               NULL CONSTRAINT FK_PaymentOrder_Employee REFERENCES core.Employee (Id),
    PartnerBankAccountId   int               NULL CONSTRAINT FK_PaymentOrder_PartnerBankAccount REFERENCES core.PartnerBankAccount (Id),
    BeneficiaryName        nvarchar(140)     NOT NULL,
    BeneficiaryIban        varchar(34)       NOT NULL,
    BeneficiaryBic         varchar(11)       NULL,
    BeneficiaryFiscalCode  varchar(20)       NULL,
    IsFreeIban             bit               NOT NULL CONSTRAINT DF_PaymentOrder_IsFreeIban DEFAULT (0),
    CurrencyCode           char(3)           NOT NULL CONSTRAINT FK_PaymentOrder_Currency REFERENCES core.Currency (Code),
    Amount                 decimal(19,2)     NOT NULL,
    RemittanceInfo         nvarchar(500)     NULL,
    EndToEndId             varchar(35)       NULL,
    BudgetObligationRef    nvarchar(50)      NULL,
    Status                 tinyint           NOT NULL CONSTRAINT DF_PaymentOrder_Status DEFAULT (1),
    ConfirmationStatus     tinyint           NULL,
    BankStatementLineId    bigint            NULL CONSTRAINT FK_PaymentOrder_BankStatementLine REFERENCES fin.BankStatementLine (Id),
    PaymentBatchId         int               NULL,
    ValidatedAt            datetime2(3)      NULL,
    ValidatedBy            int               NULL,
    ApprovedAt             datetime2(3)      NULL,
    ApprovedBy             int               NULL,
    AnafInactiveConfirmed  bit               NOT NULL CONSTRAINT DF_PaymentOrder_AnafInactiveConfirmed DEFAULT (0),
    ReversalOfId           int               NULL CONSTRAINT FK_PaymentOrder_ReversalOf REFERENCES fin.PaymentOrder (Id),
    SourceProposalId       int               NULL,
    CreatedAt              datetime2(3)      NOT NULL CONSTRAINT DF_PaymentOrder_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy              int               NOT NULL,
    ModifiedAt             datetime2(3)      NULL,
    ModifiedBy             int               NULL,
    RowVersion             rowversion        NOT NULL,
    CONSTRAINT CK_PaymentOrder_Origin CHECK (Origin BETWEEN 1 AND 4),
    CONSTRAINT CK_PaymentOrder_Purpose CHECK (Purpose BETWEEN 1 AND 4),
    CONSTRAINT CK_PaymentOrder_Status CHECK (Status BETWEEN 1 AND 3),
    CONSTRAINT CK_PaymentOrder_ConfirmationStatus CHECK (ConfirmationStatus IS NULL OR ConfirmationStatus BETWEEN 1 AND 3),
    CONSTRAINT CK_PaymentOrder_Amount CHECK (Amount <> 0 AND (ReversalOfId IS NOT NULL OR Amount > 0)),
    CONSTRAINT CK_PaymentOrder_Beneficiary CHECK (PartnerId IS NULL OR EmployeeId IS NULL),
    CONSTRAINT CK_PaymentOrder_ValidatedHasNumber CHECK (Status = 1 OR (Number IS NOT NULL AND ValidatedBy IS NOT NULL)),
    CONSTRAINT CK_PaymentOrder_Execution CHECK (RequestedExecutionDate >= DocumentDate)
);
GO

CREATE UNIQUE INDEX UX_PaymentOrder_CompanyId_Number ON fin.PaymentOrder (CompanyId, Number) WHERE Number IS NOT NULL;
CREATE UNIQUE INDEX UX_PaymentOrder_EndToEndId ON fin.PaymentOrder (EndToEndId) WHERE EndToEndId IS NOT NULL;
CREATE INDEX IX_PaymentOrder_CompanyId_DocumentDate ON fin.PaymentOrder (CompanyId, DocumentDate);
CREATE INDEX IX_PaymentOrder_TreasuryAccountId_Unconfirmed ON fin.PaymentOrder (TreasuryAccountId, ConfirmationStatus) WHERE ConfirmationStatus = 1;
CREATE INDEX IX_PaymentOrder_BeneficiaryIban_Amount ON fin.PaymentOrder (BeneficiaryIban, Amount);
CREATE INDEX IX_PaymentOrder_PartnerId_Status ON fin.PaymentOrder (PartnerId, Status) WHERE PartnerId IS NOT NULL;
GO

CREATE TABLE fin.PaymentOrderLine (
    Id                      bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentOrderLine PRIMARY KEY,
    PaymentOrderId          int               NOT NULL,
    LineNumber              smallint          NOT NULL,
    TreasuryOperationTypeId int               NOT NULL CONSTRAINT FK_PaymentOrderLine_TreasuryOperationType REFERENCES fin.TreasuryOperationType (Id),
    SettlementRef           nvarchar(100)     NULL,
    Amount                  decimal(19,2)     NOT NULL,
    CostCenterId            int               NULL CONSTRAINT FK_PaymentOrderLine_CostCenter REFERENCES core.DimensionValue (Id),
    ProjectId               int               NULL CONSTRAINT FK_PaymentOrderLine_Project REFERENCES core.DimensionValue (Id),
    CONSTRAINT FK_PaymentOrderLine_PaymentOrder FOREIGN KEY (PaymentOrderId) REFERENCES fin.PaymentOrder (Id) ON DELETE CASCADE,
    CONSTRAINT UQ_PaymentOrderLine_Order_LineNumber UNIQUE (PaymentOrderId, LineNumber),
    CONSTRAINT CK_PaymentOrderLine_AmountNotZero CHECK (Amount <> 0)
);
GO

ALTER TABLE fin.BankStatementLinePart ADD CONSTRAINT FK_BankStatementLinePart_PaymentOrder FOREIGN KEY (PaymentOrderId) REFERENCES fin.PaymentOrder (Id);
GO

-- Liniile unui OP ciorna, inlocuite la salvare (fin.usp_PaymentOrder_Save, SP-07).
CREATE TYPE fin.PaymentOrderLineList AS TABLE (
    LineNumber              smallint      NOT NULL PRIMARY KEY,
    TreasuryOperationTypeId int           NOT NULL,
    SettlementRef           nvarchar(100) NULL,
    Amount                  decimal(19,2) NOT NULL,
    CostCenterId            int           NULL,
    ProjectId               int           NULL
);
GO

-- Lista de Id-uri cu versiuni (aprobarea pe lot).
CREATE TYPE fin.IdVersionList AS TABLE (
    Id         int       NOT NULL PRIMARY KEY,
    RowVersion binary(8) NULL
);
GO

-- Setarile Finantelor pe firma (F2: Fin.PaymentOrder.RequireApproval, Fin.PaymentOrder.ConfirmationAlertDays, Fin.Bank.AutoValidateLevel).
-- O setare lipsa are valoarea implicita din specificatie (FinanceSettings in C#).
CREATE TABLE fin.CompanySetting (
    CompanyId  int           NOT NULL CONSTRAINT FK_CompanySetting_Company REFERENCES core.Company (Id),
    Code       varchar(60)   NOT NULL,
    Value      nvarchar(200) NOT NULL,
    ModifiedAt datetime2(3)  NOT NULL CONSTRAINT DF_CompanySetting_ModifiedAt DEFAULT (sysutcdatetime()),
    ModifiedBy int           NOT NULL,
    CONSTRAINT PK_CompanySetting PRIMARY KEY (CompanyId, Code)
);
GO

-- Partile unei linii pot confirma un OP (PaymentOrderId): tipul tabel se reface; procedura care il foloseste se recreeaza din
-- Programmability dupa migrari.
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
    BudgetObligationRef      nvarchar(50)  NULL,
    CostCenterId             int           NULL,
    ProjectId                int           NULL,
    Note                     nvarchar(500) NULL
);
GO
