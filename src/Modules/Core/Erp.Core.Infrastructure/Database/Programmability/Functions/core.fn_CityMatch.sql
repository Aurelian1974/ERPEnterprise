CREATE OR ALTER FUNCTION core.fn_CityMatch
(
    @CountyCode nvarchar(3),
    @City       nvarchar(100)
)
RETURNS TABLE
AS
RETURN
    -- Localitatea SIRUTA (nivelul 3, activa) din judet cu numele dat, fara diacritice si majuscule; un rand doar daca e unica
    -- (in acelasi judet pot fi mai multe sate cu acelasi nume). In Bucuresti, „Sector 2”, „Sectorul 2” sau „SECTOR2” = sectorul 2.
    -- Prefixele uzuale („Mun.”, „Municipiul”, „Oraș”, „Sat”, „Comuna”) se ignora.
    -- Baza e case sensitive (SQL-24): prefixele si „sector” se compara fara majuscule si diacritice.
    WITH Input AS (
        SELECT LTRIM(RTRIM(@City)) COLLATE Latin1_General_100_CI_AI_SC_UTF8 AS Text
    ),
    Clean AS (
        SELECT LTRIM(CASE
                   WHEN i.Text LIKE N'municipiul %' THEN SUBSTRING(i.Text, 12, 100)
                   WHEN i.Text LIKE N'mun. %' OR i.Text LIKE N'mun %' THEN SUBSTRING(i.Text, CHARINDEX(N' ', i.Text) + 1, 100)
                   WHEN i.Text LIKE N'oras %' THEN SUBSTRING(i.Text, 6, 100)
                   WHEN i.Text LIKE N'comuna %' THEN SUBSTRING(i.Text, 8, 100)
                   WHEN i.Text LIKE N'sat %' THEN SUBSTRING(i.Text, 5, 100)
                   ELSE i.Text END) AS Name,
               CASE WHEN @CountyCode = N'B' AND i.Text LIKE N'%sector%' THEN TRY_CAST(RIGHT(i.Text, 1) AS tinyint) END AS Sector
        FROM Input AS i
    ),
    Candidates AS (
        SELECT c.SirutaCode, c.Name, c.PostalCode
        FROM core.City AS c
        CROSS JOIN Clean AS x
        WHERE c.CountyCode = @CountyCode AND c.[Level] = 3 AND c.IsActive = 1
          AND (c.NameSearch = x.Name
               OR (x.Sector BETWEEN 1 AND 6 AND c.Kind = 6 AND c.Name = N'Sectorul ' + CAST(x.Sector AS nvarchar(1))))
    )
    SELECT c.SirutaCode, c.Name, c.PostalCode
    FROM Candidates AS c
    WHERE (SELECT COUNT(*) FROM Candidates) = 1;
