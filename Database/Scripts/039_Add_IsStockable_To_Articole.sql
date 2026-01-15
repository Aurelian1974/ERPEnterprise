PRINT '=== Începere migrare 039_Add_IsStockable_To_Articole ===';

-- Add IsStockable column to Articole table
ALTER TABLE dbo.Articole
ADD IsStockable BIT NOT NULL DEFAULT 1;

-- Add index for performance if needed
CREATE INDEX IX_Articole_IsStockable ON dbo.Articole(IsStockable);

PRINT '✅ Coloana IsStockable adăugată în tabela Articole';

PRINT '=== Migrare 039_Add_IsStockable_To_Articole completă ===';