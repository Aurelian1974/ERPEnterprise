-- Formularele declaratiilor (02.02, C4 3.3; MIG-06). BudgetKind: bugetul obligatiei de plata (1 TVA, 2 impozit pe profit), null fara plata.
SET NOCOUNT ON;

MERGE fin.TaxForm AS target
USING (VALUES
    ('D300', N'Decont de TVA', 1), ('D394', N'Declarație informativă privind livrările și achizițiile pe teritoriul național', NULL),
    ('D390', N'Declarație recapitulativă privind livrările și achizițiile intracomunitare', NULL), ('D406', N'Fișierul standard de control fiscal (SAF-T)', NULL),
    ('D100', N'Declarație privind obligațiile de plată la bugetul de stat', 2), ('D101', N'Declarație privind impozitul pe profit', 2)
) AS source (Code, Name, BudgetKind)
ON target.Code = source.Code
WHEN MATCHED AND (target.Name <> source.Name OR ISNULL(target.BudgetKind, 0) <> ISNULL(source.BudgetKind, 0)) THEN
    UPDATE SET Name = source.Name, BudgetKind = source.BudgetKind
WHEN NOT MATCHED BY TARGET THEN INSERT (Code, Name, BudgetKind) VALUES (source.Code, source.Name, source.BudgetKind);
