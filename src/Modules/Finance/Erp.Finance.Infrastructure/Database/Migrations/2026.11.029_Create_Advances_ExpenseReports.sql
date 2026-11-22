-- Avansurile spre decontare si deconturile de cheltuieli (02.02, F1 3.5, 3.7, 4.4): tipurile de cheltuieli si diurna (cu data de
-- vigoare), avansul acordat unui angajat, decontul cu justificativele lui; legaturile documentelor de casa spre avans si decont.

-- Tipurile de cheltuieli si diurna (F1 3.7): sumele si plafonul neimpozabil al diurnei au data de vigoare (P6).
CREATE TABLE fin.ExpenseType (
    Id                     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExpenseType PRIMARY KEY,
    Code                   nvarchar(30)      NOT NULL,
    Name                   nvarchar(200)     NOT NULL,
    DefaultVatRateCode     nvarchar(10)      NULL,
    VatDeductibleOnReceipt bit               NOT NULL CONSTRAINT DF_ExpenseType_VatDeductibleOnReceipt DEFAULT (0),
    IsPerDiem              bit               NOT NULL CONSTRAINT DF_ExpenseType_IsPerDiem DEFAULT (0),
    DailyAmount            decimal(19,2)     NULL,
    NonTaxableDailyLimit   decimal(19,2)     NULL,
    CurrencyCode           char(3)           NULL CONSTRAINT FK_ExpenseType_Currency REFERENCES core.Currency (Code),
    CountryCode            char(2)           NULL CONSTRAINT FK_ExpenseType_Country REFERENCES core.Country (Code),
    ValidFrom              date              NOT NULL,
    ValidTo                date              NULL,
    CounterGlAccountCode   nvarchar(20)      NULL,
    IsSystem               bit               NOT NULL CONSTRAINT DF_ExpenseType_IsSystem DEFAULT (0),
    IsActive               bit               NOT NULL CONSTRAINT DF_ExpenseType_IsActive DEFAULT (1),
    CreatedAt              datetime2(3)      NOT NULL CONSTRAINT DF_ExpenseType_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy              int               NOT NULL,
    ModifiedAt             datetime2(3)      NULL,
    ModifiedBy             int               NULL,
    RowVersion             rowversion        NOT NULL,
    CodeKey                AS UPPER(Code) PERSISTED,
    CONSTRAINT CK_ExpenseType_PerDiem CHECK (IsPerDiem = 0 OR (DailyAmount IS NOT NULL AND NonTaxableDailyLimit IS NOT NULL)),
    CONSTRAINT CK_ExpenseType_Validity CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
GO

CREATE UNIQUE INDEX UX_ExpenseType_CodeKey_ValidFrom ON fin.ExpenseType (CodeKey, ValidFrom);
GO

-- Avansul spre decontare (F1 3.5). Status: 0 De platit (dispozitia de plata e ciorna; adaugat la implementare), 1 Acordat, 2 Decontat
-- partial, 3 Decontat, 4 Depasit termenul, 5 Stornat. Soldul = ce datoreaza angajatul (negativ = ce ii datoreaza firma).
CREATE TABLE fin.CashAdvance (
    Id                      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashAdvance PRIMARY KEY,
    CompanyId               int               NOT NULL CONSTRAINT FK_CashAdvance_Company REFERENCES core.Company (Id),
    EmployeeId              int               NOT NULL CONSTRAINT FK_CashAdvance_Employee REFERENCES core.Employee (Id),
    Purpose                 tinyint           NOT NULL,
    CurrencyCode            char(3)           NOT NULL CONSTRAINT FK_CashAdvance_Currency REFERENCES core.Currency (Code),
    Amount                  decimal(19,2)     NOT NULL,
    Description             nvarchar(500)     NULL,
    GrantedByCashDocumentId int               NULL CONSTRAINT FK_CashAdvance_GrantedByCashDocument REFERENCES fin.CashDocument (Id),
    TripStartDate           date              NULL,
    TripEndDate             date              NULL,
    SettlementDueDate       date              NOT NULL,
    SettledAmount           decimal(19,2)     NOT NULL CONSTRAINT DF_CashAdvance_SettledAmount DEFAULT (0),
    ReturnedAmount          decimal(19,2)     NOT NULL CONSTRAINT DF_CashAdvance_ReturnedAmount DEFAULT (0),
    PaidDifference          decimal(19,2)     NOT NULL CONSTRAINT DF_CashAdvance_PaidDifference DEFAULT (0),
    Balance                 AS (Amount - SettledAmount - ReturnedAmount + PaidDifference) PERSISTED,
    Status                  tinyint           NOT NULL CONSTRAINT DF_CashAdvance_Status DEFAULT (0),
    OverdueConfirmed        bit               NOT NULL CONSTRAINT DF_CashAdvance_OverdueConfirmed DEFAULT (0),
    CreatedAt               datetime2(3)      NOT NULL CONSTRAINT DF_CashAdvance_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy               int               NOT NULL,
    ModifiedAt              datetime2(3)      NULL,
    ModifiedBy              int               NULL,
    RowVersion              rowversion        NOT NULL,
    CONSTRAINT CK_CashAdvance_Purpose CHECK (Purpose BETWEEN 1 AND 4),
    CONSTRAINT CK_CashAdvance_Status CHECK (Status BETWEEN 0 AND 5),
    CONSTRAINT CK_CashAdvance_Amount CHECK (Amount > 0),
    CONSTRAINT CK_CashAdvance_Trip CHECK (TripEndDate IS NULL OR TripStartDate IS NULL OR TripEndDate >= TripStartDate)
);
GO

CREATE INDEX IX_CashAdvance_CompanyId_EmployeeId_Status ON fin.CashAdvance (CompanyId, EmployeeId, Status);
CREATE INDEX IX_CashAdvance_Open ON fin.CashAdvance (CompanyId, SettlementDueDate) WHERE Status IN (1, 2, 4);
GO

-- Decontul de cheltuieli (F1 3.7). Status: 1 Ciorna, 2 Trimis spre aprobare, 3 Aprobat, 4 Validat, 5 Stornat.
CREATE TABLE fin.ExpenseReport (
    Id                       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExpenseReport PRIMARY KEY,
    CompanyId                int               NOT NULL CONSTRAINT FK_ExpenseReport_Company REFERENCES core.Company (Id),
    EmployeeId               int               NOT NULL CONSTRAINT FK_ExpenseReport_Employee REFERENCES core.Employee (Id),
    CashAdvanceId            int               NULL CONSTRAINT FK_ExpenseReport_CashAdvance REFERENCES fin.CashAdvance (Id),
    Number                   nvarchar(30)      NULL,
    DocumentDate             date              NOT NULL,
    CurrencyCode             char(3)           NOT NULL CONSTRAINT FK_ExpenseReport_Currency REFERENCES core.Currency (Code),
    TotalJustified           decimal(19,2)     NOT NULL CONSTRAINT DF_ExpenseReport_TotalJustified DEFAULT (0),
    AdvanceAmount            decimal(19,2)     NOT NULL CONSTRAINT DF_ExpenseReport_AdvanceAmount DEFAULT (0),
    Difference               AS (TotalJustified - AdvanceAmount) PERSISTED,
    Description              nvarchar(500)     NULL,
    Status                   tinyint           NOT NULL CONSTRAINT DF_ExpenseReport_Status DEFAULT (1),
    SubmittedAt              datetime2(3)      NULL,
    SubmittedBy              int               NULL,
    ApprovedAt               datetime2(3)      NULL,
    ApprovedBy               int               NULL,
    ValidatedAt              datetime2(3)      NULL,
    ValidatedBy              int               NULL,
    SettlementCashDocumentId int               NULL CONSTRAINT FK_ExpenseReport_SettlementCashDocument REFERENCES fin.CashDocument (Id),
    CreatedAt                datetime2(3)      NOT NULL CONSTRAINT DF_ExpenseReport_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy                int               NOT NULL,
    ModifiedAt               datetime2(3)      NULL,
    ModifiedBy               int               NULL,
    RowVersion               rowversion        NOT NULL,
    CONSTRAINT CK_ExpenseReport_Status CHECK (Status BETWEEN 1 AND 5),
    CONSTRAINT CK_ExpenseReport_ValidatedHasNumber CHECK (Status < 4 OR Number IS NOT NULL)
);
GO

CREATE UNIQUE INDEX UX_ExpenseReport_CompanyId_Number ON fin.ExpenseReport (CompanyId, Number) WHERE Number IS NOT NULL;
CREATE UNIQUE INDEX UX_ExpenseReport_CashAdvanceId ON fin.ExpenseReport (CashAdvanceId) WHERE CashAdvanceId IS NOT NULL AND Status <> 5;
CREATE INDEX IX_ExpenseReport_CompanyId_DocumentDate ON fin.ExpenseReport (CompanyId, DocumentDate);
CREATE INDEX IX_ExpenseReport_EmployeeId_Status ON fin.ExpenseReport (EmployeeId, Status);
GO

CREATE TABLE fin.ExpenseReportLine (
    Id                bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExpenseReportLine PRIMARY KEY,
    ExpenseReportId   int               NOT NULL,
    LineNumber        smallint          NOT NULL,
    ExpenseTypeId     int               NOT NULL CONSTRAINT FK_ExpenseReportLine_ExpenseType REFERENCES fin.ExpenseType (Id),
    JustificationKind tinyint           NOT NULL,
    DocumentNo        nvarchar(50)      NULL,
    DocumentDate      date              NULL,
    SupplierFiscalCode nvarchar(20)     NULL,
    SupplierInvoiceId int               NULL,
    Amount            decimal(19,2)     NOT NULL,
    VatAmount         decimal(19,2)     NOT NULL CONSTRAINT DF_ExpenseReportLine_VatAmount DEFAULT (0),
    CostCenterId      int               NULL CONSTRAINT FK_ExpenseReportLine_CostCenter REFERENCES core.DimensionValue (Id),
    ProjectId         int               NULL CONSTRAINT FK_ExpenseReportLine_Project REFERENCES core.DimensionValue (Id),
    Days              decimal(9,2)      NULL,
    DailyAmount       decimal(19,2)     NULL,
    NonTaxableAmount  decimal(19,2)     NULL,
    TaxableAmount     decimal(19,2)     NULL,
    Note              nvarchar(500)     NULL,
    -- Liniile se sterg cu ciorna (SQL-15); SupplierInvoiceId e factura din Achizitii, fara FK (alta schema, P1).
    CONSTRAINT FK_ExpenseReportLine_ExpenseReport FOREIGN KEY (ExpenseReportId) REFERENCES fin.ExpenseReport (Id) ON DELETE CASCADE,
    CONSTRAINT UQ_ExpenseReportLine_ExpenseReportId_LineNumber UNIQUE (ExpenseReportId, LineNumber),
    CONSTRAINT CK_ExpenseReportLine_JustificationKind CHECK (JustificationKind BETWEEN 1 AND 4),
    CONSTRAINT CK_ExpenseReportLine_Amount CHECK (Amount >= 0 AND VatAmount >= 0)
);
GO

-- Legaturile documentelor de casa spre avans si decont (coloanele exista din 2026.11.028).
ALTER TABLE fin.CashDocument ADD CONSTRAINT FK_CashDocument_CashAdvance FOREIGN KEY (CashAdvanceId) REFERENCES fin.CashAdvance (Id);
ALTER TABLE fin.CashDocument ADD CONSTRAINT FK_CashDocument_ExpenseReport FOREIGN KEY (ExpenseReportId) REFERENCES fin.ExpenseReport (Id);
CREATE INDEX IX_CashDocument_CashAdvanceId ON fin.CashDocument (CashAdvanceId) WHERE CashAdvanceId IS NOT NULL;
GO

CREATE TYPE fin.ExpenseReportLineList AS TABLE (
    LineNumber         smallint      NOT NULL PRIMARY KEY,
    ExpenseTypeId      int           NOT NULL,
    JustificationKind  tinyint       NOT NULL,
    DocumentNo         nvarchar(50)  NULL,
    DocumentDate       date          NULL,
    SupplierFiscalCode nvarchar(20)  NULL,
    SupplierInvoiceId  int           NULL,
    Amount             decimal(19,2) NOT NULL,
    VatAmount          decimal(19,2) NOT NULL,
    CostCenterId       int           NULL,
    ProjectId          int           NULL,
    Days               decimal(9,2)  NULL,
    DailyAmount        decimal(19,2) NULL,
    NonTaxableAmount   decimal(19,2) NULL,
    TaxableAmount      decimal(19,2) NULL,
    Note               nvarchar(500) NULL
);
GO
