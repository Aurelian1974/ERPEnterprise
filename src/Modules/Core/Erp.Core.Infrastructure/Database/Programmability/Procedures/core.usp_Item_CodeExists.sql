CREATE OR ALTER PROCEDURE core.usp_Item_CodeExists
    @Code nvarchar(50)
AS
BEGIN
    -- Codul de articol e deja folosit? La alocarea din seria „Core.Item” un cod luat se sare (seria nu e fiscala).
    SET NOCOUNT ON;

    SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM core.Item WHERE CodeKey = UPPER(@Code)) THEN 1 ELSE 0 END AS bit);
END;
