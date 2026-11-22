CREATE OR ALTER PROCEDURE fin.usp_TreasuryAccountUser_IsAssigned
    @TreasuryAccountId int,
    @UserId            int,
    @Day               date,
    @ForValidation     bit
AS
BEGIN
    -- Casierul e atribuit casei in ziua data (F1 2 regula 7) si, pentru validare, are dreptul de validare (CanValidate, F1 8).
    SET NOCOUNT ON;

    SELECT CAST(CASE WHEN EXISTS (
        SELECT 1 FROM fin.TreasuryAccountUser
        WHERE TreasuryAccountId = @TreasuryAccountId AND UserId = @UserId
          AND (ValidFrom IS NULL OR ValidFrom <= @Day) AND (ValidTo IS NULL OR ValidTo >= @Day)
          AND (@ForValidation = 0 OR CanValidate = 1)) THEN 1 ELSE 0 END AS bit);
END;
