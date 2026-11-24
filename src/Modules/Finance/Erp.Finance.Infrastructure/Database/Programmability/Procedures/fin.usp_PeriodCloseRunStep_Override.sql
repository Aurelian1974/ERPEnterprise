CREATE OR ALTER PROCEDURE fin.usp_PeriodCloseRunStep_Override
    @CompanyId  int,
    @RunId      bigint,
    @StepCode   varchar(40),
    @Reason     nvarchar(500),
    @UserId     int
AS
BEGIN
    -- Trece peste un pas blocant esuat, cu motiv (C3 decizia 4): doar pasii care permit (51909) si doar pe ultima rulare a lunii; rularea poate
    -- inchide luna daca nu mai are pasi blocanti esuati netrecuti.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NULLIF(LTRIM(@Reason), N'') IS NULL
        THROW 51907, N'Trecerea peste un pas cere un motiv.', 1;

    BEGIN TRANSACTION;
    DECLARE @Period int = (SELECT PeriodId FROM fin.PeriodCloseRun WITH (UPDLOCK) WHERE Id = @RunId AND CompanyId = @CompanyId);
    IF @Period IS NULL OR @RunId <> (SELECT MAX(Id) FROM fin.PeriodCloseRun WHERE CompanyId = @CompanyId AND PeriodId = @Period)
        THROW 51903, N'Rularea nu mai e cea curentă; rulați din nou lista.', 1;

    UPDATE fin.PeriodCloseRunStep SET OverrideReason = @Reason, OverriddenBy = @UserId, OverriddenAt = SYSUTCDATETIME()
    WHERE RunId = @RunId AND StepCode = @StepCode AND Outcome = 2 AND Severity = 1 AND AllowOverride = 1;
    IF @@ROWCOUNT = 0
        THROW 51909, N'Peste acest pas nu se poate trece: nu a eșuat, nu e blocant sau nu permite trecerea (coada „De contat”, evenimentele în tranzit).', 1;

    UPDATE fin.PeriodCloseRun
    SET CanClose = CASE WHEN EXISTS (SELECT 1 FROM fin.PeriodCloseRunStep WHERE RunId = @RunId AND Severity = 1 AND Outcome = 2 AND OverrideReason IS NULL) THEN 0 ELSE 1 END
    WHERE Id = @RunId;
    COMMIT TRANSACTION;
END;
