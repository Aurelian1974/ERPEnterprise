PRINT '=== Începere migrare 047_Stock ===';

IF OBJECT_ID('dbo.Stock', 'U') IS NOT NULL
    DROP TABLE dbo.Stock;
GO

CREATE TABLE dbo.Stock (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ItemId UNIQUEIDENTIFIER NOT NULL, -- FK to Articole
    LocationId UNIQUEIDENTIFIER NOT NULL, -- FK to Locations
    Quantity DECIMAL(18,2) NOT NULL DEFAULT 0,
    ReservedQuantity DECIMAL(18,2) NOT NULL DEFAULT 0,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CreatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIME2 NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL,
    OwnerCompanyId UNIQUEIDENTIFIER NULL,
    OwnerWorkPlaceId UNIQUEIDENTIFIER NULL,
    OwnerLocationId UNIQUEIDENTIFIER NULL
);

-- Add unique constraint on ItemId + LocationId (one stock record per item per location)
ALTER TABLE dbo.Stock
ADD CONSTRAINT UQ_Stock_Item_Location UNIQUE (ItemId, LocationId);

-- Add foreign key constraints
ALTER TABLE dbo.Stock
ADD CONSTRAINT FK_Stock_Item FOREIGN KEY (ItemId) REFERENCES dbo.Articole(Id);

ALTER TABLE dbo.Stock
ADD CONSTRAINT FK_Stock_Location FOREIGN KEY (LocationId) REFERENCES dbo.Locations(Id);

ALTER TABLE dbo.Stock
ADD CONSTRAINT FK_Stock_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.Users(Id);

ALTER TABLE dbo.Stock
ADD CONSTRAINT FK_Stock_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES dbo.Users(Id);

-- Add indexes for performance
CREATE INDEX IX_Stock_ItemId ON dbo.Stock(ItemId);
CREATE INDEX IX_Stock_LocationId ON dbo.Stock(LocationId);
CREATE INDEX IX_Stock_IsActive ON dbo.Stock(IsActive);
CREATE INDEX IX_Stock_OwnerCompanyId ON dbo.Stock(OwnerCompanyId);

-- Add check constraints
ALTER TABLE dbo.Stock
ADD CONSTRAINT CK_Stock_Quantity_NotNegative CHECK (Quantity >= 0);

ALTER TABLE dbo.Stock
ADD CONSTRAINT CK_Stock_ReservedQuantity_NotNegative CHECK (ReservedQuantity >= 0);

ALTER TABLE dbo.Stock
ADD CONSTRAINT CK_Stock_Reserved_LE_Quantity CHECK (ReservedQuantity <= Quantity);

PRINT '✅ Tabela Stock creată';

PRINT '=== Migrare 047_Stock completă ===';