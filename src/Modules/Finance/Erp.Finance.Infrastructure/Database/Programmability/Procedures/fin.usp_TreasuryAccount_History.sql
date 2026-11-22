CREATE OR ALTER PROCEDURE fin.usp_TreasuryAccount_History
    @CompanyId int,
    @Id        int,
    @Top       int = 200
AS
BEGIN
    -- Tab-ul Istoric al contului de trezorerie (02.02, F1 11; macheta f1-01): modificarile contului din jurnalul de audit, cele mai noi
    -- intai. Contul altei firme nu intoarce nimic.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount WHERE Id = @Id AND CompanyId = @CompanyId)
        RETURN;

    SELECT TOP (@Top) a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, u.DisplayName AS UserName, a.Changes
    FROM core.AuditEntry AS a
    JOIN core.[User] AS u ON u.Id = a.UserId
    WHERE a.EntityType = N'Fin.TreasuryAccount' AND a.EntityId = @Id
    ORDER BY a.Id DESC;
END;
