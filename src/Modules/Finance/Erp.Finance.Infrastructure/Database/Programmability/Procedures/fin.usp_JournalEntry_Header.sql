CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_Header
    @Id  bigint
AS
BEGIN
    -- Antetul unei note dupa validare sau storno, pentru evenimentul NotaContabilaValidata (C2 5.1).
    SET NOCOUNT ON;

    SELECT e.Id, e.CompanyId, j.Code AS JournalCode, e.Number, e.AccountingDate, e.SourceKind, e.SourceDocumentType, e.SourceDocumentId, e.TotalDebit,
           e.RowVersion
    FROM fin.JournalEntry AS e JOIN fin.Journal AS j ON j.Id = e.JournalId
    WHERE e.Id = @Id;
END;
