CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_Check
    @EntryId  bigint
AS
BEGIN
    -- Verificarile unei note la validare (C2 3.1 regulile 2 si 8): arunca prima eroare gasita de fin.usp_JournalEntry_FirstError (51843–51847).
    SET NOCOUNT ON;

    DECLARE @Code int, @Message nvarchar(400);
    EXEC fin.usp_JournalEntry_FirstError @EntryId = @EntryId, @ErrorCode = @Code OUTPUT, @ErrorMessage = @Message OUTPUT;
    IF @Code <> 0
        THROW @Code, @Message, 1;
END;
