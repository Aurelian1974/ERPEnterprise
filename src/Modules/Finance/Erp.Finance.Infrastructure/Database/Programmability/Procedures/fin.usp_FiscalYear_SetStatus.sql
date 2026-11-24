CREATE OR ALTER PROCEDURE fin.usp_FiscalYear_SetStatus
    @CompanyId  int,
    @YearId     int,
    @Status     tinyint,
    @Reason     nvarchar(500) = NULL,
    @UserId     int
AS
BEGIN
    -- Starea anului (C3 3.1, 4.3): 2 inchis (lunile toate inchise, 51911), 3 blocat (doar dintr-un an inchis; lunile devin blocate), deblocarea
    -- (din 3 in 2, cu motiv, 51907; lunile redevin inchise). Dreptul de deblocare se verifica in aplicatie.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    DECLARE @Current tinyint = (SELECT Status FROM fin.FiscalYear WITH (UPDLOCK, HOLDLOCK) WHERE Id = @YearId AND CompanyId = @CompanyId);
    IF @Current IS NULL
        THROW 51901, N'Anul financiar nu există.', 1;
    IF @Status = 2 AND @Current = 1 AND EXISTS (SELECT 1 FROM fin.AccountingPeriod WHERE FiscalYearId = @YearId AND Status < 3)
        THROW 51911, N'Anul se închide după ce toate lunile lui sunt închise.', 1;
    IF @Status = 3 AND @Current <> 2
        THROW 51911, N'Se blochează doar un an închis.', 1;
    IF @Status = 2 AND @Current = 3 AND NULLIF(LTRIM(@Reason), N'') IS NULL
        THROW 51907, N'Deblocarea anului cere un motiv.', 1;

    UPDATE fin.FiscalYear
    SET Status = @Status,
        ClosedAt = CASE WHEN @Status = 2 AND @Current = 1 THEN SYSUTCDATETIME() ELSE ClosedAt END,
        ClosedBy = CASE WHEN @Status = 2 AND @Current = 1 THEN @UserId ELSE ClosedBy END,
        LockedAt = CASE WHEN @Status = 3 THEN SYSUTCDATETIME() WHEN @Current = 3 THEN NULL ELSE LockedAt END,
        LockedBy = CASE WHEN @Status = 3 THEN @UserId WHEN @Current = 3 THEN NULL ELSE LockedBy END,
        LockReason = CASE WHEN @Current = 3 THEN @Reason ELSE LockReason END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @YearId;

    IF @Status = 3
        UPDATE fin.AccountingPeriod SET Status = 4, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE FiscalYearId = @YearId;
    IF @Current = 3 AND @Status = 2
        UPDATE fin.AccountingPeriod SET Status = 3, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE FiscalYearId = @YearId;
    COMMIT TRANSACTION;
END;
