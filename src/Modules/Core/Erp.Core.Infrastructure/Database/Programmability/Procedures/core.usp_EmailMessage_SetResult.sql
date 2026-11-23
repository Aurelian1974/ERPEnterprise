CREATE OR ALTER PROCEDURE core.usp_EmailMessage_SetResult
    @Id    int,
    @Sent  bit,
    @Error nvarchar(1000) = NULL
AS
BEGIN
    -- Rezultatul unei incercari de trimitere (02.01, 4.7): trimis, sau inca de trimis cu ultima eroare si numarul de incercari.
    SET NOCOUNT ON;

    UPDATE core.EmailMessage
    SET Status = CASE WHEN @Sent = 1 THEN 2 ELSE Status END, SentAt = CASE WHEN @Sent = 1 THEN SYSUTCDATETIME() ELSE SentAt END,
        Attempts = Attempts + 1, LastError = CASE WHEN @Sent = 1 THEN NULL ELSE @Error END
    WHERE Id = @Id;
END;
