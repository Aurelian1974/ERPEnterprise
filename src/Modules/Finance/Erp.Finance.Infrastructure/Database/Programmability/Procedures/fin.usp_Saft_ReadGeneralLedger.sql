CREATE OR ALTER PROCEDURE fin.usp_Saft_ReadGeneralLedger
    @CompanyId     int,
    @JournalId     int,
    @From          date,
    @To            date,
    @AfterEntryId  bigint = 0,
    @AfterLine     int = 0,
    @Take          int = 5000
AS
BEGIN
    -- O pagina din registrul D406 (C4 3.5, NET-12): liniile notelor validate ale unui jurnal, in ordinea notei si a liniei, dupa cheia ultimei
    -- linii citite; fisierul se scrie in flux, fara sa incarce luna in memorie.
    SET NOCOUNT ON;
    SELECT TOP (@Take) e.Id AS EntryId, e.Number, e.AccountingDate, e.EntryDate, e.SourceKind, e.Description AS EntryDescription, e.SourceDocumentNumber,
        e.CreatedBy, l.LineNumber, a.Code AS AccountCode, l.Side, l.AmountRon, l.CurrencyCode, l.AmountCurrency, l.PartnerId, l.Description
    FROM fin.JournalLine AS l
    JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
    JOIN fin.GlAccount AS a ON a.Id = l.AccountId
    WHERE e.CompanyId = @CompanyId AND e.JournalId = @JournalId AND e.Status >= 2 AND e.AccountingDate BETWEEN @From AND @To
      AND (l.EntryId > @AfterEntryId OR (l.EntryId = @AfterEntryId AND l.LineNumber > @AfterLine))
    ORDER BY l.EntryId, l.LineNumber;
END;
