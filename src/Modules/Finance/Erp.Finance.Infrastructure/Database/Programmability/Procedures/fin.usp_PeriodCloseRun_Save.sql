CREATE OR ALTER PROCEDURE fin.usp_PeriodCloseRun_Save
    @CompanyId  int,
    @PeriodId   int,
    @CanClose   bit,
    @Steps      nvarchar(max),
    @UserId     int
AS
BEGIN
    -- Salveaza o rulare a listei de verificari (C3 4.1 pasul 3): pasii (JSON [{stepCode, severity, allowOverride, outcome, summary, issues,
    -- overrideReason, overriddenBy}]), cu trecerile peste pas preluate de la rularea anterioara. Intoarce Id-ul rularii.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    INSERT INTO fin.PeriodCloseRun (CompanyId, PeriodId, CanClose, StartedBy) VALUES (@CompanyId, @PeriodId, @CanClose, @UserId);
    DECLARE @RunId bigint = SCOPE_IDENTITY();
    INSERT INTO fin.PeriodCloseRunStep (RunId, StepCode, Severity, AllowOverride, Outcome, Summary, Issues, OverrideReason, OverriddenBy, OverriddenAt)
    SELECT @RunId, j.StepCode, j.Severity, j.AllowOverride, j.Outcome, j.Summary, j.Issues, j.OverrideReason, j.OverriddenBy,
        CASE WHEN j.OverrideReason IS NOT NULL THEN SYSUTCDATETIME() END
    FROM OPENJSON(@Steps) WITH (StepCode varchar(40) '$.stepCode', Severity tinyint '$.severity', AllowOverride bit '$.allowOverride', Outcome tinyint '$.outcome',
        Summary nvarchar(400) '$.summary', Issues nvarchar(max) '$.issues', OverrideReason nvarchar(500) '$.overrideReason', OverriddenBy int '$.overriddenBy') AS j;
    COMMIT TRANSACTION;

    SELECT @RunId;
END;
