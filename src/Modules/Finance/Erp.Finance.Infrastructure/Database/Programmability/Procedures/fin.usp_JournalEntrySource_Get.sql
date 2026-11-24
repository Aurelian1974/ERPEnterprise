CREATE OR ALTER PROCEDURE fin.usp_JournalEntrySource_Get
    @CompanyId  int,
    @EntryId    bigint
AS
BEGIN
    -- Contractul de contare pastrat al unei note automate (simularea pe un document real, C2 4.2, macheta c2-03).
    SET NOCOUNT ON;

    SELECT s.EventType, s.Payload
    FROM fin.JournalEntrySource AS s JOIN fin.JournalEntry AS e ON e.Id = s.EntryId
    WHERE s.EntryId = @EntryId AND e.CompanyId = @CompanyId;
END;
