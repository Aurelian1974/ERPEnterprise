CREATE OR ALTER PROCEDURE fin.usp_ProfitTax_SetPosted
    @CompanyId  int,
    @Id         int,
    @EntryId    bigint,
    @Amount     decimal(19,2),
    @UserId     int
AS
BEGIN
    -- Leaga nota 691 = 4411 de fisa (C4 4.4 pasul 2) si retine suma contata (impozitul declarat pentru trimestrele urmatoare).
    SET NOCOUNT ON;
    UPDATE fin.ProfitTaxComputation SET EntryId = @EntryId, PostedAmount = @Amount, PostedAt = SYSUTCDATETIME(), PostedBy = @UserId
    WHERE Id = @Id AND CompanyId = @CompanyId;
END;
