CREATE OR ALTER PROCEDURE core.usp_IdempotencyKey_Claim
    @UserId      int,
    @Key         nvarchar(100),
    @RequestType nvarchar(300)
AS
BEGIN
    -- 4.1: rezerva cheia de idempotenta in tranzactia comenzii. UPDLOCK + HOLDLOCK serializeaza cererile cu aceeasi cheie:
    -- a doua asteapta commit-ul primei si primeste randul existent (fara eroare de unicitate, fara document dublu).
    -- Daca randul exista, se intoarce (RequestType, ResultJson); altfel se insereaza si nu se intoarce nimic.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM core.IdempotencyKey WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId AND [Key] = @Key)
    BEGIN
        SELECT RequestType, ResultJson
        FROM core.IdempotencyKey
        WHERE UserId = @UserId AND [Key] = @Key;
        RETURN;
    END;

    INSERT INTO core.IdempotencyKey (UserId, [Key], RequestType)
    VALUES (@UserId, @Key, @RequestType);
END;
