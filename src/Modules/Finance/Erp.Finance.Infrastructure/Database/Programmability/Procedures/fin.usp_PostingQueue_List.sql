CREATE OR ALTER PROCEDURE fin.usp_PostingQueue_List
    @CompanyId  int,
    @Status     tinyint = 1
AS
BEGIN
    -- Coada „De contat” a firmei (C2 4.4, macheta c2-04), pe stare, cele mai vechi primele.
    SET NOCOUNT ON;

    SELECT Id, EventId, EventType, SourceDocumentType, SourceDocumentId, SourceDocumentNumber, AccountingDate, Reason, Details, Status, Attempts, LastAttemptAt,
           ResolvedEntryId, IgnoreReason, Payload, RowVersion
    FROM fin.PostingQueue
    WHERE CompanyId = @CompanyId AND (@Status IS NULL OR Status = @Status)
    ORDER BY AccountingDate, Id;
END;
