PRINT '=== Începere migrare 036_Articole ===';

IF OBJECT_ID('dbo.Articole', 'U') IS NOT NULL
    DROP TABLE dbo.Articole;
GO

CREATE TABLE dbo.Articole (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ArticolCode NVARCHAR(50) NOT NULL,
    ArticolName NVARCHAR(200) NOT NULL,
    Description NVARCHAR(500) NULL,
    TipArticolId UNIQUEIDENTIFIER NOT NULL,
    UnitateMasura NVARCHAR(20) NOT NULL DEFAULT 'BUC',
    PretAchizitie DECIMAL(18,2) NULL,
    PretVanzare DECIMAL(18,2) NULL,
    StocMinim DECIMAL(18,2) NULL DEFAULT 0,
    StocMaxim DECIMAL(18,2) NULL,
    IsStockable BIT NOT NULL DEFAULT 1,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT (GETDATE()),
    CreatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIME2 NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT FK_Articole_TipArticolId FOREIGN KEY (TipArticolId) REFERENCES dbo.ItemTypes(Id)
);

-- Indexes for performance
CREATE INDEX IX_Articole_ArticolCode ON dbo.Articole(ArticolCode);
CREATE INDEX IX_Articole_TipArticolId ON dbo.Articole(TipArticolId);
CREATE INDEX IX_Articole_IsActive ON dbo.Articole(IsActive);

PRINT '✅ Tabela Articole creată cu foreign key către ItemTypes';

PRINT '=== Migrare 036_Articole completă ===';