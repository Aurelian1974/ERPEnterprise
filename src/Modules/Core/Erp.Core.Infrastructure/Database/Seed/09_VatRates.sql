-- Cotele TVA livrate (02.01, 3.5; P6), doar daca lipsesc. Verificate pe 20.11.2026 (Legea 141/2025, Codul fiscal art. 291):
-- din 01.08.2025 cota standard 21% si o singura cota redusa 11%; 19%, 9% si 5% raman pentru documentele pana la 31.07.2025.
-- „Valabil de la” al cotelor vechi e inceputul istoricului folosit de aplicatie (19% e in vigoare din 01.01.2017).
-- Categoriile UBL (e-Factura): S standard, Z zero, E scutit, AE taxare inversa, K livrare intracomunitara, G export, O neimpozabil.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.VatRate AS target
USING (VALUES
    (N'S21', N'Standard',                                   21.00, 'S',  '2025-08-01', NULL),
    (N'R11', N'Redusă',                                     11.00, 'S',  '2025-08-01', NULL),
    (N'S19', N'Standard (vechi)',                           19.00, 'S',  '2017-01-01', '2025-07-31'),
    (N'R9',  N'Redusă 9% (veche)',                           9.00, 'S',  '2017-01-01', '2025-07-31'),
    (N'R5',  N'Redusă 5% (veche)',                           5.00, 'S',  '2017-01-01', '2025-07-31'),
    (N'Z0',  N'Cotă zero',                                   0.00, 'Z',  '2017-01-01', NULL),
    (N'E0',  N'Scutit fără drept de deducere',               0.00, 'E',  '2017-01-01', NULL),
    (N'AE',  N'Taxare inversă',                              0.00, 'AE', '2017-01-01', NULL),
    (N'K0',  N'Livrare intracomunitară scutită',             0.00, 'K',  '2017-01-01', NULL),
    (N'G0',  N'Export scutit cu drept de deducere',          0.00, 'G',  '2017-01-01', NULL),
    (N'O0',  N'Neimpozabil în România',                      0.00, 'O',  '2017-01-01', NULL)
) AS source (Code, Name, [Percent], Category, ValidFrom, ValidTo)
ON target.Code = source.Code AND target.ValidFrom = source.ValidFrom
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, [Percent], Category, ValidFrom, ValidTo, CreatedBy)
    VALUES (source.Code, source.Name, source.[Percent], source.Category, source.ValidFrom, source.ValidTo, 1);
