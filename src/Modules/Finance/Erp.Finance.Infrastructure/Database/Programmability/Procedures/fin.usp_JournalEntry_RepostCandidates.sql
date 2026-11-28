CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_RepostCandidates
    @CompanyId  int,
    @From       date,
    @To         date,
    @EventType  varchar(80) = NULL
AS
BEGIN
    -- Notele automate care se pot reconta (C2 4.6): validate, nestornate, cu contractul de contare pastrat, in luni care nu sunt inchise
    -- (deschise sau inchise operativ). Intai notele cu contractul, apoi liniile lor pe cod de cont, pentru previzualizarea diferentelor.
    SET NOCOUNT ON;

    DECLARE @Entries TABLE (Id bigint PRIMARY KEY);
    INSERT INTO @Entries (Id)
    SELECT e.Id
    FROM fin.JournalEntry AS e
    JOIN fin.JournalEntrySource AS s ON s.EntryId = e.Id
    WHERE e.CompanyId = @CompanyId AND e.Status = 2 AND e.SourceKind = 1 AND e.AccountingDate BETWEEN @From AND @To
      AND (@EventType IS NULL OR s.EventType = @EventType)
      AND NOT EXISTS (SELECT 1 FROM fin.AccountingPeriod AS p
                      WHERE p.CompanyId = e.CompanyId AND p.StartDate <= e.AccountingDate AND p.EndDate >= e.AccountingDate AND p.Status >= 3);

    SELECT e.Id, j.Code AS JournalCode, e.Number, e.AccountingDate, s.EventType, s.Payload, e.RowVersion
    FROM @Entries AS x
    JOIN fin.JournalEntry AS e ON e.Id = x.Id
    JOIN fin.Journal AS j ON j.Id = e.JournalId
    JOIN fin.JournalEntrySource AS s ON s.EntryId = e.Id
    ORDER BY e.AccountingDate, e.Id;

    SELECT l.EntryId, a.Code AS AccountCode, l.Side, l.AmountRon, l.PartnerId, l.EmployeeId, l.CostCenterId, l.ProjectId, l.WarehouseId, l.SiteId
    FROM @Entries AS x
    JOIN fin.JournalLine AS l ON l.EntryId = x.Id
    JOIN fin.GlAccount AS a ON a.Id = l.AccountId
    ORDER BY l.EntryId, l.LineNumber;
END;
