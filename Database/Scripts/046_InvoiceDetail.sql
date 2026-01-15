PRINT '=== Începere migrare 046_InvoiceDetail ===';

IF OBJECT_ID('dbo.InvoiceDetail', 'U') IS NOT NULL
    DROP TABLE dbo.InvoiceDetail;
GO

CREATE TABLE dbo.InvoiceDetail (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    InvoiceId UNIQUEIDENTIFIER NOT NULL,
    DocumentDetailId UNIQUEIDENTIFIER NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL,
    VATRate DECIMAL(5,2) NOT NULL DEFAULT 0,
    VATAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    LineTotal DECIMAL(18,2) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CreatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIME2 NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL,
    OwnerCompanyId UNIQUEIDENTIFIER NULL,
    OwnerWorkPlaceId UNIQUEIDENTIFIER NULL,
    OwnerLocationId UNIQUEIDENTIFIER NULL
);

-- Add foreign key constraints
ALTER TABLE dbo.InvoiceDetail
ADD CONSTRAINT FK_InvoiceDetail_Invoice FOREIGN KEY (InvoiceId) REFERENCES dbo.Invoice(Id);

ALTER TABLE dbo.InvoiceDetail
ADD CONSTRAINT FK_InvoiceDetail_DocumentDetail FOREIGN KEY (DocumentDetailId) REFERENCES dbo.DocumentDetail(Id);

ALTER TABLE dbo.InvoiceDetail
ADD CONSTRAINT FK_InvoiceDetail_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.Users(Id);

ALTER TABLE dbo.InvoiceDetail
ADD CONSTRAINT FK_InvoiceDetail_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES dbo.Users(Id);

-- Add indexes for performance
CREATE INDEX IX_InvoiceDetail_InvoiceId ON dbo.InvoiceDetail(InvoiceId);
CREATE INDEX IX_InvoiceDetail_DocumentDetailId ON dbo.InvoiceDetail(DocumentDetailId);
CREATE INDEX IX_InvoiceDetail_IsActive ON dbo.InvoiceDetail(IsActive);
CREATE INDEX IX_InvoiceDetail_OwnerCompanyId ON dbo.InvoiceDetail(OwnerCompanyId);

-- Add check constraints
ALTER TABLE dbo.InvoiceDetail
ADD CONSTRAINT CK_InvoiceDetail_UnitPrice_Positive CHECK (UnitPrice >= 0);

ALTER TABLE dbo.InvoiceDetail
ADD CONSTRAINT CK_InvoiceDetail_VATRate_Valid CHECK (VATRate >= 0 AND VATRate <= 100);

ALTER TABLE dbo.InvoiceDetail
ADD CONSTRAINT CK_InvoiceDetail_VATAmount_Positive CHECK (VATAmount >= 0);

ALTER TABLE dbo.InvoiceDetail
ADD CONSTRAINT CK_InvoiceDetail_LineTotal_Positive CHECK (LineTotal >= 0);

PRINT '✅ Tabela InvoiceDetail creată';

PRINT '=== Migrare 046_InvoiceDetail completă ===';