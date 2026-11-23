CREATE OR ALTER PROCEDURE fin.usp_OpeningBalance_Lookup
    @CompanyId int,
    @Keys      nvarchar(max)
AS
BEGIN
    -- Validarea importului de solduri initiale (F3 4.9; 02.01, 4.8 pasul 3): pentru fiecare rand (JSON: row, partner, role, key) partenerul
    -- gasit dupa cod sau CUI (cu sau fara „RO”) si daca partida exista deja (importata inainte); apoi monedele cunoscute.
    SET NOCOUNT ON;

    DECLARE @K TABLE (RowNumber int PRIMARY KEY, PartnerKey nvarchar(50), Role tinyint, LineKey nvarchar(50));
    INSERT INTO @K (RowNumber, PartnerKey, Role, LineKey)
    SELECT RowNumber, PartnerKey, Role, LineKey
    FROM OPENJSON(@Keys) WITH (RowNumber int '$.row', PartnerKey nvarchar(50) '$.partner', Role tinyint '$.role', LineKey nvarchar(50) '$.key');

    DECLARE @Partners TABLE (PartnerKey nvarchar(50) PRIMARY KEY, PartnerId int);
    INSERT INTO @Partners (PartnerKey, PartnerId)
    SELECT d.PartnerKey,
        COALESCE((SELECT TOP (1) p.Id FROM core.Partner AS p WHERE p.Code = d.PartnerKey ORDER BY p.Id),
                 (SELECT TOP (1) p.Id FROM core.Partner AS p
                  WHERE p.FiscalCode IS NOT NULL AND (p.FiscalCode = d.PartnerKey OR p.FiscalCode = d.Stripped OR p.FiscalCode = N'RO' + d.Stripped)
                  ORDER BY p.IsActive DESC, p.Id))
    FROM (SELECT DISTINCT PartnerKey, CASE WHEN UPPER(LEFT(PartnerKey, 2)) = N'RO' THEN SUBSTRING(PartnerKey, 3, 50) ELSE PartnerKey END AS Stripped
          FROM @K WHERE PartnerKey IS NOT NULL) AS d;

    SELECT k.RowNumber, p.PartnerId,
        CAST(CASE WHEN EXISTS (SELECT 1 FROM fin.OpenItem AS o
                               WHERE o.SourceDocumentType = 'Finance.OpeningBalance' AND o.SourceDocumentId = p.PartnerId AND o.SourceLineKey = k.LineKey
                                 AND o.Role = k.Role AND o.CompanyId = @CompanyId) THEN 1 ELSE 0 END AS bit) AS AlreadyImported
    FROM @K AS k
    LEFT JOIN @Partners AS p ON p.PartnerKey = k.PartnerKey
    ORDER BY k.RowNumber;

    SELECT Code FROM core.Currency;
END;
