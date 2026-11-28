CREATE OR ALTER PROCEDURE fin.usp_Saft_Check
    @CompanyId  int,
    @From       date,
    @To         date
AS
BEGIN
    -- Verificarile dinaintea fisierului D406 (C4 3.5, 4.3 pasul 1), pe categorii: conturile cu rulaj fara cont SAF-T (51951), partenerii fara cod
    -- fiscal sau partenerii UE fara cod de TVA cu prefixul tarii (51952), notele validate fara numar.
    SET NOCOUNT ON;
    SELECT CAST(1 AS tinyint) AS Category, 'FIN_SAFT_ACCOUNT_CODE_MISSING' AS Code, CONCAT(N'Contul ', a.Code, N' ', a.Name, N' nu are cont SAF-T.') AS Message,
        a.Code AS Reference
    FROM fin.GlAccount AS a
    WHERE EXISTS (SELECT 1 FROM fin.JournalLine AS l JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
                  WHERE l.AccountId = a.Id AND e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @From AND @To)
      AND NOT EXISTS (SELECT 1 FROM fin.ufn_SaftAccountCode(a.Id))
    UNION ALL
    SELECT CAST(2 AS tinyint), 'FIN_SAFT_PARTNER_INVALID',
        CASE WHEN NULLIF(LTRIM(p.FiscalCode), N'') IS NULL THEN CONCAT(N'Partenerul ', p.Name, N' nu are cod fiscal.')
             ELSE CONCAT(N'Partenerul UE ', p.Name, N' nu are cod de TVA cu prefixul țării.') END,
        p.Code
    FROM core.Partner AS p
    WHERE p.Id IN (SELECT l.PartnerId FROM fin.JournalLine AS l JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
                   WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @From AND @To AND l.PartnerId IS NOT NULL
                   UNION SELECT v.PartnerId FROM fin.VatEntry AS v WHERE v.CompanyId = @CompanyId AND v.DocumentDate BETWEEN @From AND @To)
      AND ((p.PartnerType <> 4 AND NULLIF(LTRIM(p.FiscalCode), N'') IS NULL)
        OR (p.PartnerType = 2 AND p.FiscalCode NOT LIKE '[A-Z][A-Z]%'))
    UNION ALL
    SELECT CAST(3 AS tinyint), 'FIN_SAFT_ENTRY_NUMBER_MISSING', CONCAT(N'Nota din ', CONVERT(char(10), e.AccountingDate, 104), N' nu are număr.'), CAST(e.Id AS nvarchar(20))
    FROM fin.JournalEntry AS e WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @From AND @To AND e.Number IS NULL
    ORDER BY 1, 4;
END;
