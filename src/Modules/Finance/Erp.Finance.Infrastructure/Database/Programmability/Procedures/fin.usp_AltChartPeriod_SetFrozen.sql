CREATE OR ALTER PROCEDURE fin.usp_AltChartPeriod_SetFrozen
    @ChartId      int,
    @CompanyId    int,
    @PeriodYear   smallint,
    @PeriodMonth  tinyint,
    @Frozen       bit,
    @UserId       int
AS
BEGIN
    -- Ingheata sau dezgheata o luna a unei firme intr-un plan alternativ (C1 3.6): luna inghetata nu se mai recalculeaza la schimbarea regulilor.
    -- Doar planurile alternative ale firmei (51812 pentru planul romanesc, 51801 pentru plan inexistent sau fara firma).
    SET NOCOUNT ON;

    DECLARE @Kind tinyint = (SELECT c.Kind FROM fin.ChartOfAccounts AS c JOIN fin.ChartOfAccountsCompany AS cc ON cc.ChartId = c.Id
                             WHERE c.Id = @ChartId AND cc.CompanyId = @CompanyId);
    IF @Kind IS NULL
        THROW 51801, N'Planul nu există sau nu e al firmei.', 1;
    IF @Kind = 1
        THROW 51812, N'Planul românesc nu se îngheață.', 1;

    IF @Frozen = 1
        INSERT INTO fin.AltChartPeriodFreeze (ChartId, CompanyId, PeriodYear, PeriodMonth, FrozenBy)
        SELECT @ChartId, @CompanyId, @PeriodYear, @PeriodMonth, @UserId
        WHERE NOT EXISTS (SELECT 1 FROM fin.AltChartPeriodFreeze WHERE ChartId = @ChartId AND CompanyId = @CompanyId AND PeriodYear = @PeriodYear
                          AND PeriodMonth = @PeriodMonth);
    ELSE
        DELETE FROM fin.AltChartPeriodFreeze WHERE ChartId = @ChartId AND CompanyId = @CompanyId AND PeriodYear = @PeriodYear AND PeriodMonth = @PeriodMonth;
END;
