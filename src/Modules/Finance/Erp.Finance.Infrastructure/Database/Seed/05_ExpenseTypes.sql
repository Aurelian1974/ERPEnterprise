-- Tipurile de cheltuieli livrate (02.02, F1 3.7; MIG-06): cheltuielile uzuale ale unui decont si diurna interna. Diurna (50 lei/zi) si
-- plafonul ei neimpozabil (57,50 lei/zi) sunt valorile din macheta validata f1-06; se verifica cu HG 714/2018 si Codul fiscal in vigoare
-- inainte de fiecare livrare, iar o modificare se livreaza ca rand nou cu ValidFrom. Simbolurile de cont sunt doar referinte pentru
-- partea contabila. MERGE pe (Code, ValidFrom) doar pentru tipurile livrate (IsSystem = 1).
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE fin.ExpenseType AS target
USING (VALUES
    (N'CAZARE',      N'Cazare',                   0, CAST(NULL AS decimal(19,2)), CAST(NULL AS decimal(19,2)), N'625'),
    (N'TRANSPORT',   N'Transport',                0, NULL, NULL, N'624'),
    (N'COMBUSTIBIL', N'Combustibil',              0, NULL, NULL, N'3022'),
    (N'PARCARE',     N'Parcare',                  0, NULL, NULL, N'628'),
    (N'TAXI',        N'Taxi',                     0, NULL, NULL, N'624'),
    (N'CONSUMABILE', N'Materiale consumabile',    0, NULL, NULL, N'3028'),
    (N'DIVERSE',     N'Alte cheltuieli',          0, NULL, NULL, N'628'),
    (N'DIURNA-INT',  N'Diurnă internă',           1, 50.00, 57.50, N'625')
) AS source (Code, Name, IsPerDiem, DailyAmount, NonTaxableDailyLimit, CounterGlAccountCode)
ON target.CodeKey = UPPER(source.Code) AND target.ValidFrom = '20240101'
WHEN MATCHED AND target.IsSystem = 1 AND (target.Name <> source.Name OR target.IsPerDiem <> source.IsPerDiem
        OR ISNULL(target.DailyAmount, -1) <> ISNULL(source.DailyAmount, -1)
        OR ISNULL(target.NonTaxableDailyLimit, -1) <> ISNULL(source.NonTaxableDailyLimit, -1)
        OR ISNULL(target.CounterGlAccountCode, N'') <> ISNULL(source.CounterGlAccountCode, N'')) THEN
    UPDATE SET Name = source.Name, IsPerDiem = source.IsPerDiem, DailyAmount = source.DailyAmount,
        NonTaxableDailyLimit = source.NonTaxableDailyLimit, CounterGlAccountCode = source.CounterGlAccountCode, ModifiedAt = SYSUTCDATETIME(),
        ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, IsPerDiem, DailyAmount, NonTaxableDailyLimit, CurrencyCode, CountryCode, ValidFrom, CounterGlAccountCode, IsSystem, CreatedBy)
    VALUES (source.Code, source.Name, source.IsPerDiem, source.DailyAmount, source.NonTaxableDailyLimit,
        CASE WHEN source.IsPerDiem = 1 THEN 'RON' END, CASE WHEN source.IsPerDiem = 1 THEN 'RO' END, '20240101', source.CounterGlAccountCode, 1, 1);
