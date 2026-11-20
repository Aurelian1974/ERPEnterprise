CREATE OR ALTER PROCEDURE core.usp_Partner_CodeExists
    @Code nvarchar(30)
AS
BEGIN
    -- Codul e deja folosit (fara diferenta de majuscule)? La alocarea din serie, un cod luat (de alta firma cu acelasi prefix
    -- sau introdus manual) se sare: seria codurilor de partener nu e fiscala, golurile sunt permise.
    SET NOCOUNT ON;

    SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM core.Partner WHERE CodeKey = UPPER(@Code)) THEN 1 ELSE 0 END AS bit);
END;
