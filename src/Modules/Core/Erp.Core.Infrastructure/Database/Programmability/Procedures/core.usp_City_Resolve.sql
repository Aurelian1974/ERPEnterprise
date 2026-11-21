CREATE OR ALTER PROCEDURE core.usp_City_Resolve
    @CountryCode    char(2) = NULL,
    @CountyCode     nvarchar(3) = NULL,
    @City           nvarchar(100) = NULL OUTPUT,
    @CitySirutaCode int = NULL OUTPUT
AS
BEGIN
    -- Localitatea unei adrese (02.01, 3.5), apelata din procedurile de salvare ale firmei, punctului de lucru, locatiei, adreselor
    -- firmei, partenerului si adreselor lui. Codul ales din lista trebuie sa fie o localitate activa din judetul adresei (50022);
    -- textul devine numele oficial. Fara cod, pentru Romania se cauta localitatea dupa nume (unica in judet): importurile, completarea
    -- de la ANAF si adresele vechi se leaga singure, iar un nume care nu se gaseste ramane text, fara cod. In strainatate, fara cod.
    SET NOCOUNT ON;

    IF @CitySirutaCode IS NOT NULL
    BEGIN
        DECLARE @Official nvarchar(100);
        SELECT @Official = c.Name
        FROM core.City AS c
        WHERE c.SirutaCode = @CitySirutaCode AND c.[Level] = 3 AND c.IsActive = 1 AND c.CountyCode = @CountyCode AND @CountryCode = 'RO';
        IF @Official IS NULL
            THROW 50022, N'Localitatea aleasă nu există în nomenclatorul SIRUTA sau nu aparține județului.', 1;
        SET @City = @Official;
        RETURN;
    END;

    IF @CountryCode = 'RO' AND @CountyCode IS NOT NULL AND @City IS NOT NULL
        SELECT @CitySirutaCode = m.SirutaCode, @City = m.Name
        FROM core.fn_CityMatch(@CountyCode, @City) AS m;
END;
