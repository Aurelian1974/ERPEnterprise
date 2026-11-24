CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_ControlledAccounts
    @Id  bigint
AS
BEGIN
    -- Daca nota foloseste conturi tinute de module (ManualPosting = 0, mostenit de analitice; C2 3.8, decizia 5).
    SET NOCOUNT ON;

    SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM fin.JournalLine AS l JOIN fin.GlAccount AS a ON a.Id = l.AccountId WHERE l.EntryId = @Id AND a.ManualPosting = 0)
                THEN 1 ELSE 0 END AS bit);
END;
