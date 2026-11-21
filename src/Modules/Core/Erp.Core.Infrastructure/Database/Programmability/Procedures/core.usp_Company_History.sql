CREATE OR ALTER PROCEDURE core.usp_Company_History
    @Id  int,
    @Top int = 200
AS
BEGIN
    -- Tab-ul Istoric al fisei firmei (02.01, 11): auditul firmei si al punctelor de lucru, locatiilor, adreselor, persoanelor de
    -- contact, conturilor bancare, seriilor si dimensiunilor ei, cele mai noi intai.
    SET NOCOUNT ON;

    SELECT TOP (@Top) a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, u.DisplayName AS UserName, a.Changes
    FROM core.AuditEntry AS a
    JOIN core.[User] AS u ON u.Id = a.UserId
    WHERE (a.EntityType = N'Core.Company' AND a.EntityId = @Id)
       OR (a.EntityType = N'Core.Branch' AND a.EntityId IN (SELECT Id FROM core.Branch WHERE CompanyId = @Id))
       OR (a.EntityType = N'Core.Location' AND a.EntityId IN (SELECT Id FROM core.Location WHERE CompanyId = @Id))
       OR (a.EntityType = N'Core.CompanyAddress' AND a.EntityId IN (SELECT Id FROM core.CompanyAddress WHERE CompanyId = @Id))
       OR (a.EntityType = N'Core.CompanyContact' AND a.EntityId IN (SELECT Id FROM core.CompanyContact WHERE CompanyId = @Id))
       OR (a.EntityType = N'Core.CompanyBankAccount' AND a.EntityId IN (SELECT Id FROM core.CompanyBankAccount WHERE CompanyId = @Id))
       OR (a.EntityType = N'Core.NumberSeries' AND a.EntityId IN (SELECT Id FROM core.NumberSeries WHERE CompanyId = @Id))
       OR (a.EntityType = N'Core.DimensionValue' AND a.EntityId IN (SELECT Id FROM core.DimensionValue WHERE CompanyId = @Id))
    ORDER BY a.Id DESC;
END;
