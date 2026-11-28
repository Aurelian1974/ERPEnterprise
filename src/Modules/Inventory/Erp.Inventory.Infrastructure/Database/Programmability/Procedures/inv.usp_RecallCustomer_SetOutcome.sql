CREATE OR ALTER PROCEDURE inv.usp_RecallCustomer_SetOutcome
    @CompanyId int,
    @Id        bigint,
    @Outcome   tinyint,
    @Note      nvarchar(400) = NULL,
    @UserId    int
AS
BEGIN
    -- Rezultatul la un client al retragerii (02.05, S4 3.6 pasul 6): returnat, distrus la client, nu mai are.
    SET NOCOUNT ON;
    UPDATE c SET Outcome = @Outcome, OutcomeNote = @Note, OutcomeAt = SYSUTCDATETIME(), OutcomeBy = @UserId
    FROM inv.RecallCustomer AS c JOIN inv.Recall AS r ON r.Id = c.RecallId AND r.CompanyId = @CompanyId AND r.Status = 2
    WHERE c.Id = @Id;
    IF @@ROWCOUNT = 0
        THROW 54001, N'Clientul retragerii nu există sau retragerea nu e deschisă.', 1;
END;
