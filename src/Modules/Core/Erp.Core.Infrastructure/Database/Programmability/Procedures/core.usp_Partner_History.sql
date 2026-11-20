CREATE OR ALTER PROCEDURE core.usp_Partner_History
    @Id  int,
    @Top int = 200
AS
BEGIN
    -- Tab-ul Istoric al fisei (02.01, 11): intrarile de audit ale partenerului si ale adreselor, conturilor si contactelor lui,
    -- cele mai noi intai. Changes nu contine date personale (NET-18); citirile CNP-ului apar ca actiune separata.
    SET NOCOUNT ON;

    SELECT TOP (@Top) a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, u.DisplayName AS UserName, a.Changes
    FROM core.AuditEntry AS a
    JOIN core.[User] AS u ON u.Id = a.UserId
    WHERE (a.EntityType = N'Core.Partner' AND a.EntityId = @Id)
       OR (a.EntityType = N'Core.PartnerAddress' AND a.EntityId IN (SELECT Id FROM core.PartnerAddress WHERE PartnerId = @Id))
       OR (a.EntityType = N'Core.PartnerBankAccount' AND a.EntityId IN (SELECT Id FROM core.PartnerBankAccount WHERE PartnerId = @Id))
       OR (a.EntityType = N'Core.PartnerContact' AND a.EntityId IN (SELECT Id FROM core.PartnerContact WHERE PartnerId = @Id))
    ORDER BY a.Id DESC;
END;
