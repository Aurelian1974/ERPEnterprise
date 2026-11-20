CREATE OR ALTER PROCEDURE core.usp_Item_History
    @Id  int,
    @Top int = 200
AS
BEGIN
    -- Tab-ul Istoric al fisei articolului: auditul articolului si al codurilor lui de bare, cele mai noi intai.
    SET NOCOUNT ON;

    SELECT TOP (@Top) a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, u.DisplayName AS UserName, a.Changes
    FROM core.AuditEntry AS a
    JOIN core.[User] AS u ON u.Id = a.UserId
    WHERE (a.EntityType = N'Core.Item' AND a.EntityId = @Id)
       OR (a.EntityType = N'Core.ItemBarcode' AND a.EntityId IN (SELECT Id FROM core.ItemBarcode WHERE ItemId = @Id))
    ORDER BY a.Id DESC;
END;
