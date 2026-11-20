CREATE OR ALTER PROCEDURE core.usp_ItemLookups_Get
    @CompanyId int = 0
AS
BEGIN
    -- Nomenclatoarele articolelor (fisa articolului si Configurare › Nomenclatoare): cote TVA, UM, seturi de UM cu liniile lor,
    -- grupe de articole; UsageCount = cate articole le folosesc (o cota sau o UM folosita nu se mai modifica).
    SET NOCOUNT ON;

    SELECT v.Id, v.Code, v.Name, v.[Percent], v.Category, v.ValidFrom, v.ValidTo,
           (SELECT COUNT(*) FROM core.Item AS i WHERE i.DefaultVatRateId = v.Id) AS UsageCount, v.RowVersion
    FROM core.VatRate AS v
    ORDER BY CASE WHEN v.ValidTo IS NULL THEN 0 ELSE 1 END, v.[Percent] DESC, v.Code, v.ValidFrom DESC;

    SELECT u.Id, u.Code, u.Name, u.UneceCode, u.IsActive,
           (SELECT COUNT(*) FROM core.UomSetLine AS l WHERE l.UomId = u.Id) + (SELECT COUNT(*) FROM core.ItemUom AS x WHERE x.UomId = u.Id)
           + (SELECT COUNT(*) FROM core.ItemBarcode AS b WHERE b.UomId = u.Id) AS UsageCount, u.RowVersion
    FROM core.UnitOfMeasure AS u
    ORDER BY u.Code;

    SELECT s.Id, s.Code, s.Name, s.IsActive, (SELECT COUNT(*) FROM core.Item AS i WHERE i.UomSetId = s.Id) AS UsageCount, s.RowVersion
    FROM core.UomSet AS s
    ORDER BY s.Name;

    SELECT l.UomSetId, l.UomId, l.FactorToBase, l.IsBase, l.IsDefaultSales, l.IsDefaultPurchase, l.IsDefaultPos, l.SortOrder
    FROM core.UomSetLine AS l
    ORDER BY l.UomSetId, l.SortOrder, l.FactorToBase;

    SELECT g.Id, g.Code, g.Name, g.ParentId, g.SortOrder, g.IsActive, (SELECT COUNT(*) FROM core.Item AS i WHERE i.ItemGroupId = g.Id) AS UsageCount,
           g.RowVersion
    FROM core.ItemGroup AS g
    ORDER BY g.SortOrder, g.Name;

    -- Proprietatile articolelor valabile pentru firma curenta, cu optiunile lor, si utilizatorii activi.
    SELECT d.Id, d.Code, d.Name, d.DataType, d.AppliesTo, d.Section, d.SortOrder, d.IsRequired, d.IsFilterable
    FROM core.PropertyDefinition AS d
    WHERE d.EntityType = 'Item' AND d.IsActive = 1 AND (d.CompanyId IS NULL OR d.CompanyId = @CompanyId)
    ORDER BY d.Section, d.SortOrder, d.Name;

    SELECT o.Id, o.PropertyDefinitionId, o.Code, o.Label, o.IsActive
    FROM core.PropertyOption AS o
    JOIN core.PropertyDefinition AS d ON d.Id = o.PropertyDefinitionId
    WHERE d.EntityType = 'Item' AND d.IsActive = 1 AND (d.CompanyId IS NULL OR d.CompanyId = @CompanyId)
    ORDER BY o.PropertyDefinitionId, o.SortOrder, o.Label;

    SELECT Id, DisplayName FROM core.[User] WHERE IsActive = 1 ORDER BY DisplayName;
END;
