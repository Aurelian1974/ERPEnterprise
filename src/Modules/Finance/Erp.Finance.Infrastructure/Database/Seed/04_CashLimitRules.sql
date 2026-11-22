-- Plafoanele legale de numerar livrate (02.02, F1 3.6; Legea 70/2015 cu modificarile OUG 98/2023 si OUG 115/2023). Valorile sunt cele
-- confirmate de Aurelian (15.11.2026): 5.000 / 10.000 / 50.000 lei. Data de vigoare 01.01.2024 si forma in vigoare a legii se verifica pe
-- legislatie.just.ro inainte de fiecare livrare; o modificare legislativa se livreaza ca rand nou cu ValidFrom, nu prin schimbarea celui vechi.
-- MERGE doar pe plafoanele legale (CompanyId NULL); cele proprii ale firmelor nu se ating (MIG-06).
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE fin.CashLimitRule AS target
USING (VALUES
    ('IncasarePJ',      5000.00,  1, N'Legea 70/2015: încasări de la persoane juridice, PFA, profesii liberale'),
    ('IncasarePF',      10000.00, 1, N'Legea 70/2015: încasări de la persoane fizice'),
    ('PlataPJ',         5000.00,  1, N'Legea 70/2015: plăți către persoane juridice, PFA, profesii liberale'),
    ('PlataPF',         10000.00, 1, N'Legea 70/2015: plăți către persoane fizice (fără salarii, avansuri și deconturi legale)'),
    ('AvansPersoanaZi', 5000.00,  1, N'Legea 70/2015: avansuri spre decontare din casă, pe zi și persoană'),
    ('SoldCasa',        50000.00, 2, N'Legea 70/2015: soldul casei la sfârșitul zilei')
) AS source (Category, Amount, Action, LegalReference)
ON target.CompanyId IS NULL AND target.Category = source.Category AND target.CurrencyCode = 'RON' AND target.ValidFrom = '20240101'
WHEN MATCHED AND (target.Amount <> source.Amount OR target.Action <> source.Action OR ISNULL(target.LegalReference, N'') <> source.LegalReference) THEN
    UPDATE SET Amount = source.Amount, Action = source.Action, LegalReference = source.LegalReference, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (CompanyId, Category, CurrencyCode, Amount, Action, ValidFrom, LegalReference, CreatedBy)
    VALUES (NULL, source.Category, 'RON', source.Amount, source.Action, '20240101', source.LegalReference, 1);
