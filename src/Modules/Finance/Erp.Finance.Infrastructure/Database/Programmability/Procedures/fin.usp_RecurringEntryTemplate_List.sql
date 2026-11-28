CREATE OR ALTER PROCEDURE fin.usp_RecurringEntryTemplate_List
    @CompanyId  int,
    @DueOn      date = NULL
AS
BEGIN
    -- Sabloanele firmei (C2 3.8), sau doar cele active scadente la @DueOn (generarea), cu jurnalul si ultima nota generata.
    SET NOCOUNT ON;

    SELECT t.Id, t.Name, t.JournalId, j.Code AS JournalCode, t.Periodicity, t.NextDate, t.EndDate, t.Description, t.Lines, t.IsActive, t.LastEntryId,
           t.RowVersion
    FROM fin.RecurringEntryTemplate AS t
    JOIN fin.Journal AS j ON j.Id = t.JournalId
    WHERE t.CompanyId = @CompanyId
      AND (@DueOn IS NULL OR (t.IsActive = 1 AND t.NextDate <= @DueOn AND (t.EndDate IS NULL OR t.NextDate <= t.EndDate)))
    ORDER BY t.Name, t.Id;
END;
