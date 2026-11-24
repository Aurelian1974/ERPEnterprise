CREATE OR ALTER PROCEDURE fin.usp_AccountMappingRule_List
    @ChartId int
AS
BEGIN
    -- Regulile unui plan alternativ in ordinea prioritatii (C1 3.5; macheta c1-03), cu sursele din lista si tintele.
    SET NOCOUNT ON;

    SELECT r.Id, r.Priority, r.SourceKind, r.SourceAccountId, sa.Code AS SourceAccountCode, r.SourceFrom, r.SourceTo, r.CompanyId, r.Side, r.PartnerKind,
           r.PartnerGroupId, r.PartnerId, p.Name AS PartnerName, r.DimensionType, r.DimensionCondition, r.DimensionValueId, dv.Code AS DimensionValueCode,
           dv.Name AS DimensionValueName, r.ItemGroupId, r.CurrencyCode, r.BalanceSign, r.ValidFrom, r.ValidTo, r.IsActive, r.Note, r.RowVersion
    FROM fin.AccountMappingRule AS r
    LEFT JOIN fin.GlAccount AS sa ON sa.Id = r.SourceAccountId
    LEFT JOIN core.Partner AS p ON p.Id = r.PartnerId
    LEFT JOIN core.DimensionValue AS dv ON dv.Id = r.DimensionValueId
    WHERE r.TargetChartId = @ChartId
    ORDER BY r.Priority;

    SELECT ms.RuleId, a.Id AS AccountId, a.Code
    FROM fin.AccountMappingSource AS ms
    JOIN fin.AccountMappingRule AS r ON r.Id = ms.RuleId
    JOIN fin.GlAccount AS a ON a.Id = ms.AccountId
    WHERE r.TargetChartId = @ChartId
    ORDER BY ms.RuleId, a.Code;

    SELECT t.RuleId, t.TargetAccountId, a.Code, a.Name, t.PercentShare
    FROM fin.AccountMappingTarget AS t
    JOIN fin.AccountMappingRule AS r ON r.Id = t.RuleId
    JOIN fin.GlAccount AS a ON a.Id = t.TargetAccountId
    WHERE r.TargetChartId = @ChartId
    ORDER BY t.RuleId, t.SortOrder;
END;
