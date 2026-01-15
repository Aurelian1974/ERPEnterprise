PRINT '=== Începere migrare 045_DocumentDetail ===';

IF OBJECT_ID('dbo.DocumentDetail', 'U') IS NOT NULL
    DROP TABLE dbo.DocumentDetail;
GO

CREATE TABLE dbo.DocumentDetail (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    DocumentId UNIQUEIDENTIFIER NOT NULL,
    ItemId UNIQUEIDENTIFIER NOT NULL, -- FK to Articole
    Quantity DECIMAL(18,2) NOT NULL,
    UnitMeasure NVARCHAR(20) NOT NULL,
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
ALTER TABLE dbo.DocumentDetail
ADD CONSTRAINT FK_DocumentDetail_Document FOREIGN KEY (DocumentId) REFERENCES dbo.Document(Id);

ALTER TABLE dbo.DocumentDetail
ADD CONSTRAINT FK_DocumentDetail_Item FOREIGN KEY (ItemId) REFERENCES dbo.Articole(Id);

ALTER TABLE dbo.DocumentDetail
ADD CONSTRAINT FK_DocumentDetail_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.Users(Id);

ALTER TABLE dbo.DocumentDetail
ADD CONSTRAINT FK_DocumentDetail_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES dbo.Users(Id);

-- Add indexes for performance
CREATE INDEX IX_DocumentDetail_DocumentId ON dbo.DocumentDetail(DocumentId);
CREATE INDEX IX_DocumentDetail_ItemId ON dbo.DocumentDetail(ItemId);
CREATE INDEX IX_DocumentDetail_IsActive ON dbo.DocumentDetail(IsActive);
CREATE INDEX IX_DocumentDetail_OwnerCompanyId ON dbo.DocumentDetail(OwnerCompanyId);

-- Add check constraints
ALTER TABLE dbo.DocumentDetail
ADD CONSTRAINT CK_DocumentDetail_Quantity_Positive CHECK (Quantity > 0);

PRINT '✅ Tabela DocumentDetail creată';

PRINT '=== Migrare 045_DocumentDetail completă ===';