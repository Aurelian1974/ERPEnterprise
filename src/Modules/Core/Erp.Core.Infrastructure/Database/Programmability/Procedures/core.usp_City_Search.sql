CREATE OR ALTER PROCEDURE core.usp_City_Search
    @CountyCode nvarchar(3),
    @Search     nvarchar(100) = NULL,
    @SirutaCode int = NULL,
    @Top        int = 30
AS
BEGIN
    -- Lista de localitati din formularele de adresa: localitatile active (nivelul 3) ale judetului, cele al caror nume incepe cu
    -- textul cautat (fara diacritice si majuscule, SQL-25), cu unitatea lor („com. Gârbova”) si codul postal. @SirutaCode = si
    -- localitatea deja aleasa, ca sa apara in lista la editare. @Search e deja protejat pentru LIKE.
    SET NOCOUNT ON;

    SELECT TOP (@Top) c.SirutaCode, c.Name, c.Kind, p.Name AS ParentName, p.Kind AS ParentKind, c.PostalCode
    FROM core.City AS c
    LEFT JOIN core.City AS p ON p.SirutaCode = c.ParentSirutaCode
    WHERE c.CountyCode = @CountyCode AND c.[Level] = 3
      AND ((c.IsActive = 1 AND (@Search IS NULL OR c.NameSearch LIKE @Search + N'%')) OR c.SirutaCode = @SirutaCode)
    ORDER BY CASE WHEN c.SirutaCode = @SirutaCode THEN 0 ELSE 1 END,
             CASE WHEN c.Kind IN (1, 6, 9, 17, 22) THEN 0 ELSE 1 END, c.NameSearch, p.NameSearch;
END;
