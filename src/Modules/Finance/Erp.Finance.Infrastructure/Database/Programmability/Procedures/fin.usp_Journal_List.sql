CREATE OR ALTER PROCEDURE fin.usp_Journal_List
    @CompanyId int
AS
BEGIN
    -- Jurnalele firmei (C2 3.6): cele livrate si cele proprii.
    SET NOCOUNT ON;

    SELECT Id, CompanyId, Code, Name, IsSystem, IsActive FROM fin.Journal WHERE CompanyId IS NULL OR CompanyId = @CompanyId ORDER BY Code;
END;
