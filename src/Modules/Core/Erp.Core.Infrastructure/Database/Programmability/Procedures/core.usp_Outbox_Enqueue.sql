CREATE OR ALTER PROCEDURE core.usp_Outbox_Enqueue
    @EventId       uniqueidentifier,
    @EventType     nvarchar(200),
    @EventVersion  int,
    @CompanyId     int,
    @CorrelationId uniqueidentifier,
    @Payload       nvarchar(max),
    @OccurredAt    datetime2(3)
AS
BEGIN
    -- P8, 4.1: evenimentul de integrare se scrie in tranzactia documentului (deschisa de TransactionBehavior, SP-03).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    INSERT INTO core.Outbox (EventId, EventType, EventVersion, CompanyId, CorrelationId, Payload, OccurredAt)
    VALUES (@EventId, @EventType, @EventVersion, @CompanyId, @CorrelationId, @Payload, @OccurredAt);
END;
