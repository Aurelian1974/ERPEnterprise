CREATE OR ALTER PROCEDURE fin.usp_Saft_ReadMasterData
    @CompanyId  int,
    @From       date,
    @To         date
AS
BEGIN
    -- Nomenclatoarele D406 (C4 3.5): conturile planului romanesc cu contul SAF-T, partenerii perioadei (din note si din registrul de TVA),
    -- cotele de TVA si jurnalele cu note in perioada.
    SET NOCOUNT ON;
    SELECT a.Code, a.Name, s.SaftAccountCode
    FROM fin.GlAccount AS a JOIN fin.ChartOfAccounts AS c ON c.Id = a.ChartId AND c.Kind = 1
    OUTER APPLY fin.ufn_SaftAccountCode(a.Id) AS s
    WHERE a.IsPostable = 1 AND (a.CompanyId IS NULL OR a.CompanyId = @CompanyId);

    SELECT p.Id, p.Code, p.Name, p.FiscalCode, p.PartnerType, p.CountryCode, p.City, p.Address, p.IsCustomer, p.IsSupplier
    FROM core.Partner AS p
    WHERE p.Id IN (SELECT l.PartnerId FROM fin.JournalLine AS l JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
                   WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @From AND @To AND l.PartnerId IS NOT NULL
                   UNION SELECT v.PartnerId FROM fin.VatEntry AS v WHERE v.CompanyId = @CompanyId AND v.DocumentDate BETWEEN @From AND @To)
    ORDER BY p.Id;

    SELECT Code, [Percent], Category, ValidFrom, ValidTo FROM core.VatRate ORDER BY Code, ValidFrom;

    SELECT j.Id, j.Code, j.Name FROM fin.Journal AS j
    WHERE (j.CompanyId IS NULL OR j.CompanyId = @CompanyId)
      AND EXISTS (SELECT 1 FROM fin.JournalEntry AS e WHERE e.JournalId = j.Id AND e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @From AND @To)
    ORDER BY j.Code;
END;
