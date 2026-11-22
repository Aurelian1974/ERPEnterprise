-- Tipurile de operatiuni de casa livrate (02.02, F1 3.2; MIG-06). MERGE pe Code: tipurile livrate (IsSystem = 1) se actualizeaza la
-- fiecare versiune, cele ale clientului nu se ating. Directia: 1 Intrare, 2 Iesire. SettlementKind: 0 nimic, 1 facturi client,
-- 2 facturi furnizor, 3 avans spre decontare, 4 decont. CashLimitCategory: Incasare / Plata (plafonul PJ sau PF se alege dupa tipul
-- partenerului, F1 3.6 regula 3), AvansPersoanaZi, sau NULL (fara plafon: operatiuni interne, salarii, deconturi).
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE fin.TreasuryOperationType AS target
USING (VALUES
    (N'INC-CLIENT',             N'Încasare de la client',                         1, 1, 0, 0, 1, N'4111', 'Incasare'),
    (N'INC-AVANS-CLIENT',       N'Avans încasat de la client',                    1, 1, 0, 0, 0, N'419',  'Incasare'),
    (N'INC-RESTITUIRE-AVANS',   N'Restituire avans spre decontare',               1, 0, 1, 0, 3, N'542',  NULL),
    (N'INC-RIDICARE-BANCA',     N'Numerar ridicat din bancă',                     1, 0, 0, 1, 0, N'581',  NULL),
    (N'INC-VANZARI-POS',        N'Încasări din vânzări cu amănuntul (raport Z)',  1, 0, 0, 0, 0, N'4111', NULL),
    (N'INC-DIVERSE',            N'Alte încasări',                                 1, 0, 0, 0, 0, NULL,    'Incasare'),
    (N'PLATA-FURNIZOR',         N'Plată către furnizor',                          2, 1, 0, 0, 2, N'401',  'Plata'),
    (N'PLATA-AVANS-FURNIZOR',   N'Avans plătit furnizorului',                     2, 1, 0, 0, 0, N'409',  'Plata'),
    (N'PLATA-AVANS-DECONTARE',  N'Avans spre decontare acordat',                  2, 0, 1, 0, 3, N'542',  'AvansPersoanaZi'),
    (N'PLATA-DIFERENTA-DECONT', N'Diferență de plătit din decont',                2, 0, 1, 0, 4, N'542',  NULL),
    (N'PLATA-DEPUNERE-BANCA',   N'Numerar depus la bancă',                        2, 0, 0, 1, 0, N'581',  NULL),
    (N'PLATA-SALARII',          N'Plată salarii în numerar',                      2, 0, 1, 0, 0, N'421',  NULL),
    (N'PLATA-DIVERSE',          N'Alte plăți',                                    2, 0, 0, 0, 0, NULL,    'Plata')
) AS source (Code, Name, Direction, RequiresPartner, RequiresEmployee, RequiresCounterAccount, SettlementKind, CounterGlAccountCode,
             CashLimitCategory)
ON target.CodeKey = UPPER(source.Code)
WHEN MATCHED AND target.IsSystem = 1 AND (target.Name <> source.Name OR target.Direction <> source.Direction
        OR target.RequiresPartner <> source.RequiresPartner OR target.RequiresEmployee <> source.RequiresEmployee
        OR target.RequiresCounterAccount <> source.RequiresCounterAccount OR target.SettlementKind <> source.SettlementKind
        OR ISNULL(target.CounterGlAccountCode, N'') <> ISNULL(source.CounterGlAccountCode, N'')
        OR ISNULL(target.CashLimitCategory, '') <> ISNULL(source.CashLimitCategory, '')) THEN
    UPDATE SET Name = source.Name, Direction = source.Direction, RequiresPartner = source.RequiresPartner,
        RequiresEmployee = source.RequiresEmployee, RequiresCounterAccount = source.RequiresCounterAccount,
        SettlementKind = source.SettlementKind, CounterGlAccountCode = source.CounterGlAccountCode,
        CashLimitCategory = source.CashLimitCategory, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, Direction, RequiresPartner, RequiresEmployee, RequiresCounterAccount, SettlementKind, CounterGlAccountCode,
            CashLimitCategory, AppliesToCash, AppliesToBank, IsSystem, CreatedBy)
    VALUES (source.Code, source.Name, source.Direction, source.RequiresPartner, source.RequiresEmployee, source.RequiresCounterAccount,
            source.SettlementKind, source.CounterGlAccountCode, source.CashLimitCategory, 1, 0, 1, 1);
