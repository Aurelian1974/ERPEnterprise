PRINT '=== Începere migrare 044_Invoice ===';

IF OBJECT_ID('dbo.Invoice', 'U') IS NOT NULL
    DROP TABLE dbo.Invoice;
GO

CREATE TABLE dbo.Invoice (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    DocumentId UNIQUEIDENTIFIER NOT NULL,
    PartnerId UNIQUEIDENTIFIER NOT NULL,
    TotalAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    VATAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    TotalPayment DECIMAL(18,2) NOT NULL DEFAULT 0,
    Observations NVARCHAR(500) NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
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
ALTER TABLE dbo.Invoice
ADD CONSTRAINT FK_Invoice_Document FOREIGN KEY (DocumentId) REFERENCES dbo.Document(Id);

ALTER TABLE dbo.Invoice
ADD CONSTRAINT FK_Invoice_Partner FOREIGN KEY (PartnerId) REFERENCES dbo.Partners(Id);

ALTER TABLE dbo.Invoice
ADD CONSTRAINT FK_Invoice_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id);

ALTER TABLE dbo.Invoice
ADD CONSTRAINT FK_Invoice_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.Users(Id);

ALTER TABLE dbo.Invoice
ADD CONSTRAINT FK_Invoice_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES dbo.Users(Id);

-- Add indexes for performance
CREATE INDEX IX_Invoice_DocumentId ON dbo.Invoice(DocumentId);
CREATE INDEX IX_Invoice_PartnerId ON dbo.Invoice(PartnerId);
CREATE INDEX IX_Invoice_UserId ON dbo.Invoice(UserId);
CREATE INDEX IX_Invoice_IsActive ON dbo.Invoice(IsActive);
CREATE INDEX IX_Invoice_OwnerCompanyId ON dbo.Invoice(OwnerCompanyId);

-- Add check constraints
ALTER TABLE dbo.Invoice
ADD CONSTRAINT CK_Invoice_TotalAmount_Positive CHECK (TotalAmount >= 0);

ALTER TABLE dbo.Invoice
ADD CONSTRAINT CK_Invoice_VATAmount_Positive CHECK (VATAmount >= 0);

ALTER TABLE dbo.Invoice
ADD CONSTRAINT CK_Invoice_TotalPayment_Positive CHECK (TotalPayment >= 0);

PRINT '✅ Tabela Invoice creată';

PRINT '=== Migrare 044_Invoice completă ===';