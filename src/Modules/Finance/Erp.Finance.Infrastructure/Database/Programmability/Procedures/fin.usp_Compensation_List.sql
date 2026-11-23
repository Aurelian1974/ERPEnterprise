CREATE OR ALTER PROCEDURE fin.usp_Compensation_List
    @CompanyId     int,
    @Year          int = NULL,
    @Status        tinyint = NULL,
    @WithoutSigned bit = 0,
    @Search        nvarchar(100) = NULL
AS
BEGIN
    -- Lista PV-urilor de compensare (macheta f3-05), cele mai noi intai, cu partenerii lor: anul documentului, starea, fara PV semnat
    -- (validate fara scan), cautarea dupa numar sau partener.
    SET NOCOUNT ON;

    SELECT TOP (500) c.Id, c.Number, c.DocumentDate, c.Kind, c.CurrencyCode, c.TotalAmount, c.Status, CAST(CASE WHEN c.SignedAttachmentId IS NULL THEN 0 ELSE 1 END AS bit)
        AS HasSignedScan, x.Partners
    FROM fin.Compensation AS c
    OUTER APPLY (SELECT STRING_AGG(n.Name, N' ↔ ') AS Partners
                 FROM (SELECT DISTINCT p.Name FROM fin.CompensationLine AS l JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId
                       JOIN core.Partner AS p ON p.Id = o.PartnerId WHERE l.CompensationId = c.Id) AS n) AS x
    WHERE c.CompanyId = @CompanyId AND (@Year IS NULL OR YEAR(c.DocumentDate) = @Year) AND (@Status IS NULL OR c.Status = @Status)
      AND (@WithoutSigned = 0 OR (c.Status = 2 AND c.SignedAttachmentId IS NULL))
      AND (@Search IS NULL OR c.Number LIKE N'%' + @Search + N'%' OR x.Partners COLLATE Latin1_General_100_CI_AI_SC_UTF8 LIKE N'%' + @Search + N'%')
    ORDER BY c.DocumentDate DESC, c.Id DESC;
END;
