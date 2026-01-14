PRINT '=== Începere migrare 034_TipuriArticole ===';

IF OBJECT_ID('dbo.ItemTypes', 'U') IS NOT NULL
    DROP TABLE dbo.ItemTypes;
GO

CREATE TABLE dbo.ItemTypes (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ItemTypeCode NVARCHAR(50) NOT NULL,
    ItemTypeName NVARCHAR(200) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT (CAST(SYSUTCDATETIME() AT TIME ZONE 'E. Europe Standard Time' AS datetime2)),
    CreatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIME2 NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL
);

CREATE INDEX IX_ItemTypes_ItemTypeCode ON dbo.ItemTypes(ItemTypeCode);

PRINT '✅ Tabela ItemTypes creat';

PRINT '=== Migrare 034_TipuriArticole completă ===';