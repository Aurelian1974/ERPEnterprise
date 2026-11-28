CREATE OR ALTER PROCEDURE inv.usp_Warehouse_History
    @CompanyId int,
    @Id        int,
    @Top       int = 200
AS
BEGIN
    -- Tab-ul Istoric al gestiunii (02.05, S1 11; macheta s1-01): modificarile din jurnalul de audit, cele mai noi intai.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.Warehouse WHERE Id = @Id AND CompanyId = @CompanyId)
        RETURN;

    SELECT TOP (@Top) a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, u.DisplayName AS UserName, a.Changes
    FROM core.AuditEntry AS a
    JOIN core.[User] AS u ON u.Id = a.UserId
    WHERE a.EntityType = N'Inv.Warehouse' AND a.EntityId = @Id
    ORDER BY a.Id DESC;
END;
