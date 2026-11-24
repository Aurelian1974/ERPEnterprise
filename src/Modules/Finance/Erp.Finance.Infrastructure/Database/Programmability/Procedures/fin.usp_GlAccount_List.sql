CREATE OR ALTER PROCEDURE fin.usp_GlAccount_List
    @ChartId          int = NULL,
    @CompanyId        int,
    @IncludeInactive  bit = 0
AS
BEGIN
    -- Conturile unui plan (implicit cel romanesc), cele comune si ale firmei curente, in ordinea codului (C1 11, macheta c1-01).
    SET NOCOUNT ON;

    DECLARE @Chart int = ISNULL(@ChartId, (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1));

    SELECT a.Id, a.Code, a.Name, a.ParentId, a.Level, a.AccountType, a.IsPostable, a.CompanyId, a.IsOffBalance, a.IsManagement, a.StatutoryReporting,
           a.CurrencyMode, a.CurrencyCode, a.PartyRequirement, a.ManualPosting, a.SaftAccountCode, a.TaxTreatment, a.ClosingTarget, a.ValidFrom,
           a.ValidTo, a.IsSystem, a.IsActive
    FROM fin.GlAccount AS a
    WHERE a.ChartId = @Chart AND (a.CompanyId IS NULL OR a.CompanyId = @CompanyId) AND (@IncludeInactive = 1 OR a.IsActive = 1)
    ORDER BY a.Code;
END;
