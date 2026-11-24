CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_Search
    @CompanyId   int,
    @From        date,
    @To          date,
    @JournalId   int = NULL,
    @SourceKind  tinyint = NULL,
    @Status      tinyint = NULL,
    @AccountCode nvarchar(20) = NULL,
    @Search      nvarchar(100) = NULL,
    @Skip        int = 0,
    @Take        int = 100
AS
BEGIN
    -- Notele firmei pe interval (C2 11, macheta c2-01), cu filtre si paginare (SP-12): jurnal, sursa, stare, un cont (cu analiticele lui) si
    -- textul cautat in numarul documentului sau in descriere. Primul set: totalul, al doilea: pagina.
    SET NOCOUNT ON;

    DECLARE @Rows TABLE (Id bigint PRIMARY KEY);
    INSERT INTO @Rows (Id)
    SELECT e.Id
    FROM fin.JournalEntry AS e
    WHERE e.CompanyId = @CompanyId AND e.AccountingDate BETWEEN @From AND @To
      AND (@JournalId IS NULL OR e.JournalId = @JournalId) AND (@SourceKind IS NULL OR e.SourceKind = @SourceKind) AND (@Status IS NULL OR e.Status = @Status)
      AND (@Search IS NULL OR e.SourceDocumentNumber LIKE N'%' + @Search + N'%' OR e.Description LIKE N'%' + @Search + N'%')
      AND (@AccountCode IS NULL OR EXISTS (SELECT 1 FROM fin.JournalLine AS l JOIN fin.GlAccount AS a ON a.Id = l.AccountId
                                           WHERE l.EntryId = e.Id AND (a.Code = @AccountCode OR a.Code LIKE @AccountCode + N'.%'
                                                                      OR (CHARINDEX(N'.', @AccountCode) = 0 AND a.Code LIKE @AccountCode + N'[0-9]%'))));

    SELECT COUNT(*) AS Total FROM @Rows;

    SELECT e.Id, j.Code AS JournalCode, e.Number, e.AccountingDate, e.SourceKind, e.SourceDocumentType, e.SourceDocumentNumber, e.Status, e.Description,
           e.TotalDebit, (SELECT COUNT(*) FROM fin.JournalLine AS l WHERE l.EntryId = e.Id) AS LineCount,
           (SELECT STRING_AGG(x.Code, N', ') FROM (SELECT DISTINCT TOP (4) a.Code FROM fin.JournalLine AS l JOIN fin.GlAccount AS a ON a.Id = l.AccountId
                                                  WHERE l.EntryId = e.Id AND l.Side = 1 ORDER BY a.Code) AS x) AS DebitAccounts,
           (SELECT STRING_AGG(x.Code, N', ') FROM (SELECT DISTINCT TOP (4) a.Code FROM fin.JournalLine AS l JOIN fin.GlAccount AS a ON a.Id = l.AccountId
                                                  WHERE l.EntryId = e.Id AND l.Side = 2 ORDER BY a.Code) AS x) AS CreditAccounts
    FROM @Rows AS r
    JOIN fin.JournalEntry AS e ON e.Id = r.Id
    JOIN fin.Journal AS j ON j.Id = e.JournalId
    ORDER BY e.AccountingDate DESC, e.Id DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END;
