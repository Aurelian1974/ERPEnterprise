-- Tipurile de operatiuni fiscale livrate (02.02, C4 3.2; MIG-06), needitabile de client: livrarile (L-) si achizitiile (A-) pe cote si regimuri.
-- Cotele vechi (19%, 9%, 5%) raman pentru documentele pana la 31.07.2025 (Legea 141/2025).
SET NOCOUNT ON;

MERGE fin.VatOperationType AS target
USING (VALUES
    (N'L-S21', '2025-08-01', NULL, N'Livrare în țară, cota 21%', 1, 1), (N'L-S11', '2025-08-01', NULL, N'Livrare în țară, cota 11%', 1, 1),
    (N'L-S19', '2017-01-01', '2025-07-31', N'Livrare în țară, cota 19%', 1, 1), (N'L-S9', '2017-01-01', '2025-07-31', N'Livrare în țară, cota 9%', 1, 1),
    (N'L-S5', '2017-01-01', '2025-07-31', N'Livrare în țară, cota 5%', 1, 1), (N'L-Z', '2017-01-01', NULL, N'Livrare cu cota zero', 1, 1),
    (N'L-E', '2017-01-01', NULL, N'Livrare scutită fără drept de deducere', 1, 1), (N'L-IC', '2017-01-01', NULL, N'Livrare intracomunitară scutită (art. 294 alin. 2)', 1, 1),
    (N'L-AE', '2017-01-01', NULL, N'Livrare în țară cu taxare inversă (art. 331)', 1, 1), (N'L-EXP', '2017-01-01', NULL, N'Export', 1, 1),
    (N'L-O', '2017-01-01', NULL, N'Livrare neimpozabilă în România', 1, 1),
    (N'A-S21', '2025-08-01', NULL, N'Achiziție în țară, cota 21%', 2, 1), (N'A-S11', '2025-08-01', NULL, N'Achiziție în țară, cota 11%', 2, 1),
    (N'A-S19', '2017-01-01', '2025-07-31', N'Achiziție în țară, cota 19%', 2, 1), (N'A-S9', '2017-01-01', '2025-07-31', N'Achiziție în țară, cota 9%', 2, 1),
    (N'A-S5', '2017-01-01', '2025-07-31', N'Achiziție în țară, cota 5%', 2, 1), (N'A-IC-B', '2017-01-01', NULL, N'Achiziție intracomunitară de bunuri (taxare inversă)', 2, 1),
    (N'A-IC-S', '2017-01-01', NULL, N'Achiziție intracomunitară de servicii (taxare inversă)', 2, 1), (N'A-AE', '2017-01-01', NULL, N'Achiziție în țară cu taxare inversă', 2, 1),
    (N'A-IMP', '2017-01-01', NULL, N'Import', 2, 1), (N'A-NOD', '2017-01-01', NULL, N'Achiziție fără drept de deducere', 2, 0)
) AS source (Code, ValidFrom, ValidTo, Name, Direction, IsDeductible)
ON target.Code = source.Code AND target.ValidFrom = source.ValidFrom
WHEN MATCHED AND (target.Name <> source.Name OR ISNULL(target.ValidTo, '9999-12-31') <> ISNULL(source.ValidTo, '9999-12-31')) THEN
    UPDATE SET Name = source.Name, ValidTo = source.ValidTo
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, ValidFrom, ValidTo, Name, Direction, IsDeductible) VALUES (source.Code, source.ValidFrom, source.ValidTo, source.Name, source.Direction, source.IsDeductible);
