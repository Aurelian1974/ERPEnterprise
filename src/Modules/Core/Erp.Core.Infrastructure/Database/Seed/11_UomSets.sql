-- Seturile de UM livrate ca model (02.01, 3.3; macheta 05b): cele simple, cu o singura UM, si un set de bauturi cu ambalaje.
-- Se adauga doar daca lipsesc (MIG-06); clientul le modifica sau adauga altele din Configurare › Nomenclatoare.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.UomSet AS target
USING (VALUES
    (N'BUC', N'Bucăți'), (N'KG', N'Kilograme'), (N'L', N'Litri'), (N'M', N'Metri'), (N'H', N'Ore (servicii)'), (N'BAUT05', N'Băuturi 0,5 L')
) AS source (Code, Name)
ON target.Code = source.Code
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, CreatedBy) VALUES (source.Code, source.Name, 1);

-- Liniile doar pentru seturile fara linii (un set modificat de client nu se completeaza peste).
INSERT INTO core.UomSetLine (UomSetId, UomId, FactorToBase, IsBase, IsDefaultSales, IsDefaultPurchase, IsDefaultPos, SortOrder)
SELECT s.Id, u.Id, l.Factor, l.IsBase, l.Sales, l.Purchase, l.Pos, l.SortOrder
FROM (VALUES
    (N'BUC',    'H87', 1,   1, 1, 1, 1, 10),
    (N'KG',     'KGM', 1,   1, 1, 1, 1, 10),
    (N'L',      'LTR', 1,   1, 1, 1, 1, 10),
    (N'M',      'MTR', 1,   1, 1, 1, 1, 10),
    (N'H',      'HUR', 1,   1, 1, 1, 1, 10),
    (N'BAUT05', 'H87', 1,   1, 0, 0, 1, 10),
    (N'BAUT05', 'XBX', 6,   0, 1, 0, 0, 20),
    (N'BAUT05', 'XCR', 24,  0, 0, 0, 0, 30),
    (N'BAUT05', 'XPX', 480, 0, 0, 1, 0, 40)
) AS l (SetCode, UneceCode, Factor, IsBase, Sales, Purchase, Pos, SortOrder)
JOIN core.UomSet AS s ON s.Code = l.SetCode
JOIN core.UnitOfMeasure AS u ON u.UneceCode = l.UneceCode
WHERE NOT EXISTS (SELECT 1 FROM core.UomSetLine AS x WHERE x.UomSetId = s.Id);
