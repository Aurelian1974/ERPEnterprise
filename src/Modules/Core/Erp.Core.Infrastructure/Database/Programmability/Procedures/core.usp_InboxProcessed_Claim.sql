CREATE OR ALTER PROCEDURE core.usp_InboxProcessed_Claim
    @EventId  uniqueidentifier,
    @Consumer nvarchar(200)
AS
BEGIN
    -- Idempotenta consumatorului (02.01, 4.5): in tranzactia consumatorului, noteaza mesajul; intoarce 0 daca era deja procesat (atunci
    -- consumatorul nu mai ruleaza). Rollback-ul consumatorului sterge si nota, deci mesajul se reia.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    INSERT INTO core.InboxProcessed (EventId, Consumer)
    SELECT @EventId, @Consumer
    WHERE NOT EXISTS (SELECT 1 FROM core.InboxProcessed WITH (UPDLOCK, HOLDLOCK) WHERE EventId = @EventId AND Consumer = @Consumer);

    SELECT CAST(@@ROWCOUNT AS bit);
END;
