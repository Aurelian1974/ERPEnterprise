-- Categoriile de cash-flow livrate (02.02, F5 3.5; MIG-06). MERGE pe Code: cele livrate (IsSystem = 1) se actualizeaza la fiecare versiune,
-- cele ale clientului nu se ating. Directia: 1 intrari, 2 iesiri, 3 ambele.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE fin.CashFlowCategory AS target
USING (VALUES
    (N'INC-CLIENTI',      N'Încasări clienți',         1, 10),
    (N'INC-ALTE',         N'Alte încasări',            1, 20),
    (N'PLATI-FURNIZORI',  N'Plăți furnizori',          2, 30),
    (N'SALARII',          N'Salarii și contribuții',   2, 40),
    (N'TAXE',             N'Taxe și impozite',         2, 50),
    (N'CREDITE',          N'Rate și dobânzi',          2, 60),
    (N'INVESTITII',       N'Investiții',               2, 70),
    (N'CHIRII-UTILITATI', N'Chirii și utilități',      2, 80),
    (N'DIVIDENDE',        N'Dividende',                2, 90),
    (N'TRANSFERURI',      N'Transferuri între conturi', 3, 100)
) AS source (Code, Name, Direction, SortOrder)
ON target.CodeKey = UPPER(source.Code)
WHEN MATCHED AND target.IsSystem = 1 AND (target.Name <> source.Name OR target.Direction <> source.Direction OR target.SortOrder <> source.SortOrder) THEN
    UPDATE SET Name = source.Name, Direction = source.Direction, SortOrder = source.SortOrder, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, Direction, SortOrder, IsSystem, CreatedBy) VALUES (source.Code, source.Name, source.Direction, source.SortOrder, 1, 1);
