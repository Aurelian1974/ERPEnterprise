PRINT '=== Începere migrare 042_DocumentState ===';

IF OBJECT_ID('dbo.DocumentState', 'U') IS NOT NULL
    DROP TABLE dbo.DocumentState;
GO

CREATE TABLE dbo.DocumentState (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    CodStare NVARCHAR(10) NOT NULL,
    DenumireStare NVARCHAR(50) NOT NULL,
    Descriere NVARCHAR(200) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NULL
);

-- Add unique constraints
ALTER TABLE dbo.DocumentState
ADD CONSTRAINT UQ_DocumentState_CodStare UNIQUE (CodStare);

ALTER TABLE dbo.DocumentState
ADD CONSTRAINT UQ_DocumentState_DenumireStare UNIQUE (DenumireStare);

-- Add indexes for performance
CREATE INDEX IX_DocumentState_CodStare ON dbo.DocumentState(CodStare);
CREATE INDEX IX_DocumentState_IsActive ON dbo.DocumentState(IsActive);

-- Insert the 3 required states
INSERT INTO dbo.DocumentState (CodStare, DenumireStare, Descriere) VALUES
('C', 'Draft', 'Document în stadiu de ciornă, poate fi modificat'),
('V', 'Valid', 'Document validat și activ'),
('A', 'Canceled', 'Document anulat');

PRINT '✅ Tabela DocumentState creată și populată';

PRINT '=== Migrare 042_DocumentState completă ===';