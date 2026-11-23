CREATE OR ALTER PROCEDURE core.usp_Outbox_FetchBatch
    @BatchSize    int,
    @LeaseSeconds int,
    @Now          datetime2(3)
AS
BEGIN
    -- Lotul de mesaje de livrat (02.01, 4.5): cele neprocesate si scadente, in ordinea Id, cu UPDLOCK, READPAST, ca procesatorul sa poata
    -- rula si in paralel. Mesajele luate primesc un termen (lease) in NextAttemptAt, ca alt procesor sa nu le ia pana la livrare sau esec.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Claimed TABLE (Id bigint PRIMARY KEY, EventId uniqueidentifier, EventType nvarchar(200), EventVersion int, CompanyId int,
                            CorrelationId uniqueidentifier, Payload nvarchar(max), Attempts int);

    WITH due AS (
        SELECT TOP (@BatchSize) Id, EventId, EventType, EventVersion, CompanyId, CorrelationId, Payload, Attempts, NextAttemptAt
        FROM core.Outbox WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE ProcessedAt IS NULL AND Status = 0 AND (NextAttemptAt IS NULL OR NextAttemptAt <= @Now)
        ORDER BY Id)
    UPDATE due SET NextAttemptAt = DATEADD(second, @LeaseSeconds, @Now)
    OUTPUT inserted.Id, inserted.EventId, inserted.EventType, inserted.EventVersion, inserted.CompanyId, inserted.CorrelationId, inserted.Payload,
        inserted.Attempts INTO @Claimed;

    SELECT Id, EventId, EventType, EventVersion, CompanyId, CorrelationId, Payload, Attempts FROM @Claimed ORDER BY Id;
END;
