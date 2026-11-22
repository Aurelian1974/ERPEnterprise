-- Tipurile de operatiuni bancare livrate (02.02, F2 3.2; MIG-06): AppliesToBank = 1, AppliesToCash = 0. Sens: 1 Intrare, 2 Iesire,
-- 3 Ambele. Tipurile de incasare si plata catre parteneri din F1 (INC-CLIENT, PLATA-FURNIZOR, avansurile) se folosesc si la banca.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE fin.TreasuryOperationType AS target
USING (VALUES
    (N'BANK-COMISION',          N'Comision bancar',                               2, 0, 0, 0, N'627'),
    (N'BANK-DOBANDA-INC',       N'Dobândă încasată',                              1, 0, 0, 0, N'766'),
    (N'BANK-DOBANDA-PLATA',     N'Dobândă plătită',                               2, 0, 0, 0, N'666'),
    (N'BANK-TRANSFER-INTERN',   N'Virament între conturi proprii',                3, 0, 0, 1, N'581'),
    (N'BANK-SCHIMB-VALUTAR',    N'Schimb valutar între conturi proprii',          3, 0, 0, 1, N'581'),
    (N'BANK-DEPUNERE-NUMERAR',  N'Numerar depus (pereche cu DP din casă)',        1, 0, 0, 0, N'581'),
    (N'BANK-RIDICARE-NUMERAR',  N'Numerar ridicat (pereche cu DI din casă)',      2, 0, 0, 0, N'581'),
    (N'BANK-DECONTARE-CARD',    N'Decontare încasări card (POS sau online)',      1, 0, 0, 0, N'5125'),
    (N'BANK-PLATA-BUGET',       N'Plată către bugetul de stat',                   2, 0, 0, 0, NULL),
    (N'BANK-PLATA-SALARII',     N'Plată salarii',                                 2, 0, 0, 0, N'421'),
    (N'BANK-NEIDENTIFICAT',     N'Sumă de clarificat',                            3, 0, 0, 0, N'473'),
    (N'BANK-RESTITUIRE',        N'Sumă returnată de bancă (plată respinsă)',      1, 0, 0, 0, NULL)
) AS source (Code, Name, Direction, RequiresPartner, RequiresEmployee, RequiresCounterAccount, CounterGlAccountCode)
ON target.CodeKey = UPPER(source.Code)
WHEN MATCHED AND target.IsSystem = 1 AND (target.Name <> source.Name OR target.Direction <> source.Direction
        OR target.RequiresPartner <> source.RequiresPartner OR target.RequiresEmployee <> source.RequiresEmployee
        OR target.RequiresCounterAccount <> source.RequiresCounterAccount
        OR ISNULL(target.CounterGlAccountCode, N'') <> ISNULL(source.CounterGlAccountCode, N'')) THEN
    UPDATE SET Name = source.Name, Direction = source.Direction, RequiresPartner = source.RequiresPartner,
        RequiresEmployee = source.RequiresEmployee, RequiresCounterAccount = source.RequiresCounterAccount,
        CounterGlAccountCode = source.CounterGlAccountCode, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, Direction, RequiresPartner, RequiresEmployee, RequiresCounterAccount, SettlementKind, CounterGlAccountCode,
            CashLimitCategory, AppliesToCash, AppliesToBank, IsSystem, CreatedBy)
    VALUES (source.Code, source.Name, source.Direction, source.RequiresPartner, source.RequiresEmployee, source.RequiresCounterAccount,
            0, source.CounterGlAccountCode, NULL, 0, 1, 1, 1);

UPDATE fin.TreasuryOperationType
SET AppliesToBank = 1, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHERE IsSystem = 1 AND AppliesToBank = 0
  AND Code IN (N'INC-CLIENT', N'INC-AVANS-CLIENT', N'PLATA-FURNIZOR', N'PLATA-AVANS-FURNIZOR', N'PLATA-AVANS-DECONTARE',
               N'INC-RESTITUIRE-AVANS', N'PLATA-DIFERENTA-DECONT', N'INC-DIVERSE', N'PLATA-DIVERSE');
