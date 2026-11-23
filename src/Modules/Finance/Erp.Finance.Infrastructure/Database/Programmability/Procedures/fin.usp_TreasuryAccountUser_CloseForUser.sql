CREATE OR ALTER PROCEDURE fin.usp_TreasuryAccountUser_CloseForUser
    @UserId int,
    @Day    date
AS
BEGIN
    -- Utilizatorul a fost dezactivat (02.02, F1 5.2, evenimentul UtilizatorDezactivat): atribuirile lui la case se inchid cu @Day (ziua
    -- dezactivarii, inclusiv), in toate firmele; cele care nu incepusera inca se sterg (n-au fost niciodata in vigoare). Idempotent.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DELETE FROM fin.TreasuryAccountUser WHERE UserId = @UserId AND ValidFrom > @Day;
    UPDATE fin.TreasuryAccountUser
    SET ValidTo = @Day, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
    WHERE UserId = @UserId AND (ValidTo IS NULL OR ValidTo > @Day);
END;
