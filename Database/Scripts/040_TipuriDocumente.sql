PRINT '=== Începere migrare 040_TipuriDocumente ===';

IF OBJECT_ID('dbo.TipuriDocumente', 'U') IS NOT NULL
    DROP TABLE dbo.TipuriDocumente;
GO

CREATE TABLE dbo.TipuriDocumente (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    TipDocumentCode NVARCHAR(10) NOT NULL,
    TipDocumentName NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT (GETDATE()),
    UpdatedAt DATETIME2 NULL
);

-- Add unique constraints
ALTER TABLE dbo.TipuriDocumente
ADD CONSTRAINT UQ_TipuriDocumente_Code UNIQUE (TipDocumentCode);

ALTER TABLE dbo.TipuriDocumente
ADD CONSTRAINT UQ_TipuriDocumente_Name UNIQUE (TipDocumentName);

-- Add indexes for performance
CREATE INDEX IX_TipuriDocumente_TipDocumentCode ON dbo.TipuriDocumente(TipDocumentCode);
CREATE INDEX IX_TipuriDocumente_IsActive ON dbo.TipuriDocumente(IsActive);

PRINT '✅ Tabela TipuriDocumente creată';

PRINT '=== Migrare 040_TipuriDocumente completă ===';