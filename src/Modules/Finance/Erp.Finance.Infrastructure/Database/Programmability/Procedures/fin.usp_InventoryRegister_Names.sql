CREATE OR ALTER PROCEDURE fin.usp_InventoryRegister_Names
    @Partners  nvarchar(max)
AS
BEGIN
    -- Denumirile partenerilor din elementele registrului inventar (JSON [id]).
    SET NOCOUNT ON;
    SELECT Id, Name FROM core.Partner WHERE Id IN (SELECT CAST(value AS int) FROM OPENJSON(@Partners));
END;
