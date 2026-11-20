CREATE OR ALTER PROCEDURE core.usp_Item_FindForImport
    @Keys core.PartnerKeyList READONLY
AS
BEGIN
    -- Articolele existente cu acelasi cod (fara majuscule) ca randurile importate: reluarea importului actualizeaza (4.8 pasul 5).
    -- Foloseste lista de chei a partenerilor (doar coloana Code).
    SET NOCOUNT ON;

    SELECT i.Id, i.Code, i.Name, i.RowVersion
    FROM core.Item AS i
    JOIN (SELECT DISTINCT UPPER(Code) AS CodeKey FROM @Keys WHERE Code IS NOT NULL) AS k ON k.CodeKey = i.CodeKey;
END;
