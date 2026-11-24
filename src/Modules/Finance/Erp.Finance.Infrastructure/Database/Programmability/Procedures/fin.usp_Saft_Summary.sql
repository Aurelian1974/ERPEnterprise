CREATE OR ALTER PROCEDURE fin.usp_Saft_Summary
    @CompanyId  int,
    @From       date,
    @To         date
AS
BEGIN
    -- Rezumatul D406 al perioadei (C4 3.5): notele validate, liniile, rulajele, partenerii, facturile din registrul de TVA si amprenta datelor
    -- (notele si registrul), comparata la generare (51944) si la reinchiderea lunii.
    SET NOCOUNT ON;
    DECLARE @Entries int, @Lines int, @Debit decimal(19,2), @Credit decimal(19,2), @Partners int, @Invoices int, @Hash int;
    SELECT @Entries = COUNT(*), @Debit = ISNULL(SUM(TotalDebit), 0), @Credit = ISNULL(SUM(TotalCredit), 0),
        @Hash = ISNULL(CHECKSUM_AGG(CHECKSUM(Id, Number, TotalDebit, TotalCredit, Status, JournalId)), 0)
    FROM fin.JournalEntry WHERE CompanyId = @CompanyId AND Status >= 2 AND AccountingDate BETWEEN @From AND @To;
    SELECT @Lines = COUNT(*), @Partners = COUNT(DISTINCT l.PartnerId)
    FROM fin.JournalLine AS l JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
    WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @From AND @To;
    SELECT @Invoices = COUNT(DISTINCT CONCAT(SourceDocumentType, '|', SourceDocumentId)),
        @Hash = @Hash ^ ISNULL(CHECKSUM_AGG(CHECKSUM(Id, BaseRon, VatRon, OperationTypeCode)), 0)
    FROM fin.VatEntry WHERE CompanyId = @CompanyId AND DocumentDate BETWEEN @From AND @To;
    SELECT @Entries AS Entries, @Lines AS Lines, @Debit AS TotalDebit, @Credit AS TotalCredit, @Partners AS Partners, @Invoices AS Invoices, @Hash AS Hash;
END;
