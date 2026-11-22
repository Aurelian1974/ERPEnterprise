CREATE OR ALTER PROCEDURE fin.usp_CashRegisterDay_Reopen
    @TreasuryAccountId int,
    @CompanyId         int,
    @Day               date,
    @Reason            nvarchar(500),
    @UserId            int
AS
BEGIN
    -- Redeschiderea unei zile de casa inchise (02.02, F1 4.2 pasul 5): cere motiv; se redeschid si zilele ulterioare inchise, ca zilele
    -- deschise sa ramana un interval continuu (regula 3 din F1 2). Paginile alocate se pastreaza. Intoarce zilele redeschise.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount WHERE Id = @TreasuryAccountId AND CompanyId = @CompanyId AND Kind = 2)
        THROW 51023, N'Casa nu există în firma curentă.', 1;
    IF NULLIF(LTRIM(RTRIM(@Reason)), N'') IS NULL
        THROW 51021, N'Redeschiderea cere un motiv scris.', 1;

    UPDATE fin.TreasuryAccount SET ModifiedAt = ModifiedAt WHERE Id = @TreasuryAccountId;

    IF NOT EXISTS (SELECT 1 FROM fin.CashRegisterDay WITH (UPDLOCK) WHERE TreasuryAccountId = @TreasuryAccountId AND [Day] = @Day AND Status = 2)
        THROW 51021, N'Ziua nu este închisă.', 1;

    DECLARE @Reopened TABLE ([Day] date);
    UPDATE fin.CashRegisterDay
    SET Status = 1, ReopenedAt = SYSUTCDATETIME(), ReopenedBy = @UserId, ReopenReason = @Reason, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    OUTPUT inserted.[Day] INTO @Reopened
    WHERE TreasuryAccountId = @TreasuryAccountId AND [Day] >= @Day AND Status = 2;

    SELECT [Day] FROM @Reopened ORDER BY [Day] DESC;
END;
