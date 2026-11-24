CREATE OR ALTER PROCEDURE fin.usp_AltMapping_Recalculate
    @ChartId      int,
    @CompanyId    int,
    @PeriodYear   smallint,
    @PeriodMonth  tinyint,
    @UserId       int
AS
BEGIN
    -- Recalcularea unei luni a unei firme pentru un plan alternativ (C1 4.4), intr-o tranzactie: luna inghetata se sare (Skipped = 1); altfel
    -- liniile se mapeaza din nou cu regulile de azi (fin.usp_AltMapping_Apply) si se scrie un rand in fin.AltMappingRun.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM fin.AltChartPeriodFreeze WHERE ChartId = @ChartId AND CompanyId = @CompanyId AND PeriodYear = @PeriodYear AND PeriodMonth = @PeriodMonth)
    BEGIN
        SELECT CAST(1 AS bit) AS Skipped, 0 AS LineCount, 0 AS UnmappedCount;
        RETURN;
    END;

    DECLARE @Started datetime2(3) = SYSUTCDATETIME(), @Lines int, @Unmapped int;
    BEGIN TRANSACTION;
    EXEC fin.usp_AltMapping_Apply @ChartId = @ChartId, @CompanyId = @CompanyId, @PeriodYear = @PeriodYear, @PeriodMonth = @PeriodMonth,
        @LineCount = @Lines OUTPUT, @UnmappedCount = @Unmapped OUTPUT;
    INSERT INTO fin.AltMappingRun (ChartId, CompanyId, PeriodYear, PeriodMonth, LineCount, UnmappedCount, DurationMs, StartedAt, StartedBy)
    VALUES (@ChartId, @CompanyId, @PeriodYear, @PeriodMonth, @Lines, @Unmapped, DATEDIFF(MILLISECOND, @Started, SYSUTCDATETIME()), @Started, @UserId);
    COMMIT TRANSACTION;

    SELECT CAST(0 AS bit) AS Skipped, @Lines AS LineCount, @Unmapped AS UnmappedCount;
END;
