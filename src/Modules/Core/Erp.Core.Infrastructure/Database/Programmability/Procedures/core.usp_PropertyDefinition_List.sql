CREATE OR ALTER PROCEDURE core.usp_PropertyDefinition_List
    @EntityType varchar(20)
AS
BEGIN
    -- Definitiile proprietatilor unei entitati, active si inactive (Configurare › Proprietati), cu numarul de inregistrari
    -- care au valori (tipul unei proprietati folosite nu se mai schimba) si optiunile listelor.
    SET NOCOUNT ON;

    SELECT d.Id, d.EntityType, d.Code, d.Name, d.DataType, d.AppliesTo, d.Section, d.SortOrder, d.IsRequired, d.IsFilterable, d.CompanyId,
           d.IsActive, (SELECT COUNT(DISTINCT v.EntityId) FROM core.PropertyValue AS v WHERE v.PropertyDefinitionId = d.Id) AS UsageCount,
           d.RowVersion
    FROM core.PropertyDefinition AS d
    WHERE d.EntityType = @EntityType
    ORDER BY d.Section, d.SortOrder, d.Name;

    SELECT o.Id, o.PropertyDefinitionId, o.Code, o.Label, o.SortOrder, o.IsActive
    FROM core.PropertyOption AS o
    JOIN core.PropertyDefinition AS d ON d.Id = o.PropertyDefinitionId
    WHERE d.EntityType = @EntityType
    ORDER BY o.PropertyDefinitionId, o.SortOrder, o.Label;
END;
