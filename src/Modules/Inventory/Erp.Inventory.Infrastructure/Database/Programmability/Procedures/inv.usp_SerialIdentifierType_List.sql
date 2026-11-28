CREATE OR ALTER PROCEDURE inv.usp_SerialIdentifierType_List
AS
BEGIN
    -- Tipurile de identificatori ai bucatii (02.05, S4 3.4), cu masca si verificarea speciala (validate in C#).
    SET NOCOUNT ON;
    SELECT Id, Code, Name, Mask, CheckKind, IsSystem, IsActive FROM inv.SerialIdentifierType ORDER BY Code;
END;
