-- Stocuri S3.2 (02.05, S3 3.3): costurile adaugate si diferentele de pret (documentul CA). Repartizarea pe intrarile tinta se calculeaza in C#
-- (LandedCostAllocator); la validare, suma se adauga la costul intrarii, iar recalcularea cheii (CostEngine) imparte efectul intre stoc si iesirile
-- deja facute (decizia 3). Urma ramane in inv.CostAdjustment (Kind 3 sau 4, SourceDocumentType 'CA', SourceDocumentId = documentul).

CREATE TABLE inv.LandedCost (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_LandedCost PRIMARY KEY,
    CompanyId         int            NOT NULL CONSTRAINT FK_LandedCost_Company REFERENCES core.Company (Id),
    Number            nvarchar(40)   NULL,
    DocumentDate      date           NOT NULL,
    AccountingDate    date           NOT NULL,
    SourceKind        tinyint        NOT NULL,   -- 1 Factura de servicii, 2 Diferenta de pret, 3 Reducere ulterioara
    SupplierInvoiceId bigint         NULL,       -- factura din Achizitii (fara FK fizic spre purch)
    PartnerId         int            NULL CONSTRAINT FK_LandedCost_Partner REFERENCES core.Partner (Id),
    Amount            decimal(19,2)  NOT NULL,
    AllocationBasis   tinyint        NOT NULL,   -- 1 Valoare, 2 Cantitate, 3 Greutate, 4 Volum, 5 Manual
    Status            tinyint        NOT NULL CONSTRAINT DF_LandedCost_Status DEFAULT (1),   -- 1 Ciorna, 2 Validat, 4 Anulat
    Notes             nvarchar(1000) NULL,
    StockAmount       decimal(19,2)  NULL,       -- dupa validare: partea ramasa in stoc
    ExpenseAmount     decimal(19,2)  NULL,       -- dupa validare: partea dusa pe iesirile deja facute
    ValidatedAt       datetime2(3)   NULL,
    ValidatedBy       int            NULL CONSTRAINT FK_LandedCost_User_ValidatedBy REFERENCES core.[User] (Id),
    CreatedAt         datetime2(3)   NOT NULL CONSTRAINT DF_LandedCost_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int            NOT NULL,
    ModifiedAt        datetime2(3)   NULL,
    ModifiedBy        int            NULL,
    RowVersion        rowversion     NOT NULL,
    CONSTRAINT CK_LandedCost_SourceKind CHECK (SourceKind BETWEEN 1 AND 3),
    CONSTRAINT CK_LandedCost_Basis CHECK (AllocationBasis BETWEEN 1 AND 5),
    CONSTRAINT CK_LandedCost_Status CHECK (Status IN (1, 2, 4)),
    CONSTRAINT CK_LandedCost_Amount CHECK (Amount <> 0),
    CONSTRAINT CK_LandedCost_Number CHECK (Status <> 2 OR Number IS NOT NULL)
);
CREATE UNIQUE INDEX UX_LandedCost_Number ON inv.LandedCost (CompanyId, Number) WHERE Number IS NOT NULL;
CREATE INDEX IX_LandedCost_Company_Date ON inv.LandedCost (CompanyId, DocumentDate);
CREATE INDEX IX_LandedCost_SupplierInvoice ON inv.LandedCost (SupplierInvoiceId) WHERE SupplierInvoiceId IS NOT NULL;
GO

CREATE TABLE inv.LandedCostLine (
    Id              bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_LandedCostLine PRIMARY KEY,
    LandedCostId    int            NOT NULL CONSTRAINT FK_LandedCostLine_LandedCost REFERENCES inv.LandedCost (Id) ON DELETE CASCADE,
    LineNumber      int            NOT NULL,
    MovementId      bigint         NOT NULL CONSTRAINT FK_LandedCostLine_StockMovement REFERENCES inv.StockMovement (Id),
    ManualAmount    decimal(19,2)  NULL,
    AllocatedAmount decimal(19,2)  NOT NULL,
    CONSTRAINT UX_LandedCostLine_Movement UNIQUE (LandedCostId, MovementId)
);
CREATE INDEX IX_LandedCostLine_MovementId ON inv.LandedCostLine (MovementId);
GO

CREATE TYPE inv.tt_LandedCostLine AS TABLE (
    LineNumber      int            NOT NULL,
    MovementId      bigint         NOT NULL,
    ManualAmount    decimal(19,2)  NULL,
    AllocatedAmount decimal(19,2)  NOT NULL
);
GO
