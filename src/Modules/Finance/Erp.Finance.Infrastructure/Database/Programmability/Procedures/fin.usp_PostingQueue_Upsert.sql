CREATE OR ALTER PROCEDURE fin.usp_PostingQueue_Upsert
    @EventId               uniqueidentifier,
    @EventType             varchar(80),
    @CompanyId             int,
    @SourceDocumentType    varchar(50) = NULL,
    @SourceDocumentId      bigint = NULL,
    @SourceDocumentNumber  nvarchar(50) = NULL,
    @AccountingDate        date,
    @Reason                tinyint,
    @Details               nvarchar(2000) = NULL,
    @Payload               nvarchar(max)
AS
BEGIN
    -- Pune un eveniment necontat in coada „De contat” (C2 3.7) sau ii actualizeaza motivul la o noua incercare; evenimentul nu se pierde si
    -- nu blocheaza inbox-ul. Un rand ignorat sau rezolvat nu se redeschide.
    SET NOCOUNT ON;

    UPDATE fin.PostingQueue
    SET Reason = @Reason, Details = @Details, Payload = @Payload, Attempts = Attempts + 1, LastAttemptAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME()
    WHERE EventId = @EventId AND Status = 1;

    IF @@ROWCOUNT = 0 AND NOT EXISTS (SELECT 1 FROM fin.PostingQueue WHERE EventId = @EventId)
        INSERT INTO fin.PostingQueue (EventId, EventType, CompanyId, SourceDocumentType, SourceDocumentId, SourceDocumentNumber, AccountingDate, Reason, Details,
            Payload)
        VALUES (@EventId, @EventType, @CompanyId, @SourceDocumentType, @SourceDocumentId, @SourceDocumentNumber, @AccountingDate, @Reason, @Details, @Payload);
END;
