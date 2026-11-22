CREATE OR ALTER PROCEDURE fin.usp_CashLimit_GetDayTotals
    @CompanyId      int,
    @Day            date,
    @PartnerId      int = NULL,
    @EmployeeId     int = NULL,
    @PersonName     nvarchar(200) = NULL,
    @ExcludeId      int = NULL
AS
BEGIN
    -- Totalurile zilei in lei pentru plafoanele de numerar (02.02, F1 3.6): pe firma (toate casele ei), persoana si categorie, din documentele
    -- validate; documentele stornate si stornarile lor nu se numara (regula 1). Categoria Incasare / Plata devine PJ sau PF dupa tipul
    -- partenerului; angajatii si persoanele fara fisa sunt persoane fizice. Documentul curent (@ExcludeId) se adauga in C#.
    SET NOCOUNT ON;

    SELECT x.Category, SUM(x.AmountRon) AS Total
    FROM (
        SELECT CASE t.CashLimitCategory
                   WHEN 'Incasare' THEN CASE WHEN p.PartnerType IN (1, 2, 3) THEN 'IncasarePJ' ELSE 'IncasarePF' END
                   WHEN 'Plata' THEN CASE WHEN p.PartnerType IN (1, 2, 3) THEN 'PlataPJ' ELSE 'PlataPF' END
                   ELSE t.CashLimitCategory
               END AS Category,
               l.AmountRon
        FROM fin.CashDocument AS d
        JOIN fin.CashDocumentLine AS l ON l.CashDocumentId = d.Id
        JOIN fin.TreasuryOperationType AS t ON t.Id = l.TreasuryOperationTypeId
        LEFT JOIN core.Partner AS p ON p.Id = d.PartnerId
        WHERE d.CompanyId = @CompanyId AND d.DocumentDate = @Day AND d.Status = 2 AND d.ReversalOfId IS NULL
          AND (@ExcludeId IS NULL OR d.Id <> @ExcludeId)
          AND t.CashLimitCategory IS NOT NULL
          AND ((@PartnerId IS NOT NULL AND d.PartnerId = @PartnerId)
               OR (@EmployeeId IS NOT NULL AND d.EmployeeId = @EmployeeId)
               OR (@PersonName IS NOT NULL AND d.PersonName IS NOT NULL AND UPPER(d.PersonName) = UPPER(@PersonName)))
    ) AS x
    GROUP BY x.Category;
END;
