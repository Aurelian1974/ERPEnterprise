CREATE OR ALTER PROCEDURE fin.usp_PartnerFinanceProfile_Get
    @CompanyId int,
    @PartnerId int,
    @Role      tinyint
AS
BEGIN
    -- Profilul financiar al partenerului pe rol (F3 3.2); lipsa randului = valorile implicite (FIFO, dobanda legala, fara blocare).
    SET NOCOUNT ON;

    SELECT @PartnerId AS PartnerId, @Role AS Role, ISNULL(p.AutoAllocationMode, 2) AS AutoAllocationMode, ISNULL(p.PenaltyRatePerDay, 0) AS PenaltyRatePerDay,
        ISNULL(p.PenaltyGraceDays, 0) AS PenaltyGraceDays, ISNULL(p.PenaltyCapAtPrincipal, 0) AS PenaltyCapAtPrincipal,
        ISNULL(p.ApplyFixedRecoveryCost, 0) AS ApplyFixedRecoveryCost, ISNULL(p.IsPaymentBlocked, 0) AS IsPaymentBlocked, p.BlockReason,
        p.BalanceConfirmationEmail, p.RowVersion
    FROM (SELECT 1 AS One) AS x
    LEFT JOIN fin.PartnerFinanceProfile AS p ON p.CompanyId = @CompanyId AND p.PartnerId = @PartnerId AND p.Role = @Role;
END;
