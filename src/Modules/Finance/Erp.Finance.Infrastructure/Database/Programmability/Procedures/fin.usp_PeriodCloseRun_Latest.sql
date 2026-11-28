CREATE OR ALTER PROCEDURE fin.usp_PeriodCloseRun_Latest
    @CompanyId  int,
    @PeriodId   int
AS
BEGIN
    -- Ultima rulare a listei pentru o luna (C3 3.3, macheta c3-02): antetul, apoi pasii in ordinea listei, cu denumirea lor.
    SET NOCOUNT ON;

    DECLARE @RunId bigint = (SELECT TOP (1) Id FROM fin.PeriodCloseRun WHERE CompanyId = @CompanyId AND PeriodId = @PeriodId ORDER BY Id DESC);
    SELECT r.Id, r.PeriodId, r.CanClose, r.StartedAt, u.DisplayName AS StartedByName
    FROM fin.PeriodCloseRun AS r LEFT JOIN core.[User] AS u ON u.Id = r.StartedBy WHERE r.Id = @RunId;

    SELECT s.StepCode, ISNULL(d.Name, s.StepCode) AS Name, s.Severity, s.AllowOverride, s.Outcome, s.Summary, s.Issues, s.OverrideReason,
        u.DisplayName AS OverriddenByName, s.OverriddenAt
    FROM fin.PeriodCloseRunStep AS s
    LEFT JOIN fin.PeriodCloseStepDefinition AS d ON d.Code = s.StepCode
    LEFT JOIN core.[User] AS u ON u.Id = s.OverriddenBy
    WHERE s.RunId = @RunId
    ORDER BY ISNULL(d.SortOrder, 1000), s.StepCode;
END;
