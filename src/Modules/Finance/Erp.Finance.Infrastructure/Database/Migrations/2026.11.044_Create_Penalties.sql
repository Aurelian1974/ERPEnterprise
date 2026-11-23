-- Penalitatile de intarziere (02.02, F3 3.10, 4.7; macheta f3-08): ratele de referinta BNR (dobanda legala, OG 13/2011), calculele pe interval
-- si liniile lor. Status: 1 Ciorna, 2 Aprobat, 3 Facturat, 4 Anulat. Linia: Kind 1 interval penalizat, 2 suma fixa de recuperare (Legea 72/2013,
-- 40 EUR pe factura); sumele sunt in moneda liniei (a partidei, respectiv EUR la suma fixa).
CREATE TABLE fin.ReferenceRate (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReferenceRate PRIMARY KEY,
    ValidFrom   date              NOT NULL CONSTRAINT UQ_ReferenceRate_ValidFrom UNIQUE,
    Rate        decimal(9,4)      NOT NULL CONSTRAINT CK_ReferenceRate_Rate CHECK (Rate >= 0 AND Rate < 100),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_ReferenceRate_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL
);

CREATE TABLE fin.PenaltyCalculation (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PenaltyCalculation PRIMARY KEY,
    CompanyId        int               NOT NULL CONSTRAINT FK_PenaltyCalculation_Company REFERENCES core.Company (Id),
    Number           nvarchar(40)      NOT NULL,
    CalculationDate  date              NOT NULL,
    PeriodFrom       date              NOT NULL,
    PeriodTo         date              NOT NULL,
    PartnerId        int               NULL CONSTRAINT FK_PenaltyCalculation_Partner REFERENCES core.Partner (Id),
    Status           tinyint           NOT NULL CONSTRAINT DF_PenaltyCalculation_Status DEFAULT (1),
    DisputedCount    int               NOT NULL CONSTRAINT DF_PenaltyCalculation_DisputedCount DEFAULT (0),
    ApprovedAt       datetime2(3)      NULL,
    ApprovedBy       int               NULL,
    CreatedAt        datetime2(3)      NOT NULL CONSTRAINT DF_PenaltyCalculation_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int               NOT NULL,
    ModifiedAt       datetime2(3)      NULL,
    ModifiedBy       int               NULL,
    RowVersion       rowversion        NOT NULL,
    CONSTRAINT UQ_PenaltyCalculation_Number UNIQUE (CompanyId, Number),
    CONSTRAINT CK_PenaltyCalculation_Status CHECK (Status BETWEEN 1 AND 4),
    CONSTRAINT CK_PenaltyCalculation_Period CHECK (PeriodFrom <= PeriodTo)
);
CREATE INDEX IX_PenaltyCalculation_CompanyId_CalculationDate ON fin.PenaltyCalculation (CompanyId, CalculationDate);

CREATE TABLE fin.PenaltyCalculationLine (
    Id             bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PenaltyCalculationLine PRIMARY KEY,
    CalculationId  int               NOT NULL CONSTRAINT FK_PenaltyCalculationLine_Calculation REFERENCES fin.PenaltyCalculation (Id),
    LineNumber     int               NOT NULL,
    Kind           tinyint           NOT NULL,
    PartnerId      int               NOT NULL CONSTRAINT FK_PenaltyCalculationLine_Partner REFERENCES core.Partner (Id),
    OpenItemId     bigint            NOT NULL CONSTRAINT FK_PenaltyCalculationLine_OpenItem REFERENCES fin.OpenItem (Id),
    DueDate        date              NOT NULL,
    FromDate       date              NULL,
    ToDate         date              NULL,
    Days           int               NOT NULL,
    BaseAmount     decimal(19,2)     NOT NULL,
    CurrencyCode   char(3)           NOT NULL,
    RatePerDay     decimal(9,6)      NULL,
    AnnualRate     decimal(9,4)      NULL,
    PenaltyAmount  decimal(19,2)     NOT NULL,
    CONSTRAINT UQ_PenaltyCalculationLine_LineNumber UNIQUE (CalculationId, LineNumber),
    CONSTRAINT CK_PenaltyCalculationLine_Kind CHECK (Kind IN (1, 2))
);
CREATE INDEX IX_PenaltyCalculationLine_OpenItemId ON fin.PenaltyCalculationLine (OpenItemId) INCLUDE (Kind, ToDate, PenaltyAmount);

CREATE TYPE fin.PenaltyLineList AS TABLE (
    LineNumber     int            NOT NULL PRIMARY KEY,
    Kind           tinyint        NOT NULL,
    PartnerId      int            NOT NULL,
    OpenItemId     bigint         NOT NULL,
    DueDate        date           NOT NULL,
    FromDate       date           NULL,
    ToDate         date           NULL,
    Days           int            NOT NULL,
    BaseAmount     decimal(19,2)  NOT NULL,
    CurrencyCode   char(3)        NOT NULL,
    RatePerDay     decimal(9,6)   NULL,
    AnnualRate     decimal(9,4)   NULL,
    PenaltyAmount  decimal(19,2)  NOT NULL
);
