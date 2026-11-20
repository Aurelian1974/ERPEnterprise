CREATE OR ALTER PROCEDURE core.usp_PartnerLookups_Get
    @CompanyId int
AS
BEGIN
    -- Listele fisei de partener: grupele, proprietatile valabile pentru firma curenta (CompanyId NULL = tot grupul) cu
    -- optiunile lor, utilizatorii activi (proprietatile de tip utilizator) si firmele din grup (partener intra-grup).
    SET NOCOUNT ON;

    SELECT Id, Code, Name, SortOrder, IsActive, RowVersion FROM core.PartnerGroup ORDER BY SortOrder, Name;

    SELECT d.Id, d.Code, d.Name, d.DataType, d.AppliesTo, d.Section, d.SortOrder, d.IsRequired, d.IsFilterable
    FROM core.PropertyDefinition AS d
    WHERE d.EntityType = 'Partner' AND d.IsActive = 1 AND (d.CompanyId IS NULL OR d.CompanyId = @CompanyId)
    ORDER BY d.Section, d.SortOrder, d.Name;

    SELECT o.Id, o.PropertyDefinitionId, o.Code, o.Label, o.IsActive
    FROM core.PropertyOption AS o
    JOIN core.PropertyDefinition AS d ON d.Id = o.PropertyDefinitionId
    WHERE d.EntityType = 'Partner' AND d.IsActive = 1 AND (d.CompanyId IS NULL OR d.CompanyId = @CompanyId)
    ORDER BY o.PropertyDefinitionId, o.SortOrder, o.Label;

    SELECT Id, DisplayName FROM core.[User] WHERE IsActive = 1 ORDER BY DisplayName;

    SELECT Id, Code, Name FROM core.Company WHERE IsActive = 1 ORDER BY Name;
END;
