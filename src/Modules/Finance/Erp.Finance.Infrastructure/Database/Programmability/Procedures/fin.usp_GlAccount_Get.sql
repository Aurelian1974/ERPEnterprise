CREATE OR ALTER PROCEDURE fin.usp_GlAccount_Get
    @Id         int,
    @CompanyId  int
AS
BEGIN
    -- Fisa contului (C1 11, macheta c1-01): contul (fara rand daca nu exista sau e al altei firme), regulile de dimensiuni efective
    -- (ale lui sau mostenite, cu contul de la care vin), rolurile in care apare si istoricul denumirilor.
    SET NOCOUNT ON;

    SELECT a.Id, a.ChartId, a.Code, a.Name, a.ParentId, p.Code AS ParentCode, a.Level, a.AccountType, a.IsPostable, a.CompanyId, a.IsOffBalance,
           a.IsManagement, a.StatutoryReporting, a.CurrencyMode, a.CurrencyCode, a.PartyRequirement, a.ManualPosting, a.SaftAccountCode,
           a.TaxTreatment, a.ClosingTarget, a.ValidFrom, a.ValidTo, a.IsSystem, a.IsActive, a.RowVersion
    FROM fin.GlAccount AS a
    LEFT JOIN fin.GlAccount AS p ON p.Id = a.ParentId
    WHERE a.Id = @Id AND (a.CompanyId IS NULL OR a.CompanyId = @CompanyId);

    IF @@ROWCOUNT = 0
        RETURN;

    WITH Ancestors AS (
        SELECT Id, ParentId, Code, 0 AS Depth FROM fin.GlAccount WHERE Id = @Id
        UNION ALL
        SELECT g.Id, g.ParentId, g.Code, x.Depth + 1 FROM fin.GlAccount AS g JOIN Ancestors AS x ON g.Id = x.ParentId)
    SELECT t.Code AS DimensionType, t.Name AS DimensionName, r.Mode, r.FromCode, CAST(CASE WHEN r.Depth = 0 THEN 0 ELSE 1 END AS bit) AS IsInherited
    FROM core.DimensionType AS t
    CROSS APPLY (SELECT TOP (1) d.Mode, x.Code AS FromCode, x.Depth FROM Ancestors AS x
                 JOIN fin.GlAccountDimensionRule AS d ON d.AccountId = x.Id AND d.DimensionType = t.Code ORDER BY x.Depth) AS r
    ORDER BY t.SortOrder;

    SELECT r.Id, r.CompanyId, r.Role, r.PartnerKind, r.CurrencyCode, r.ValidFrom, r.ValidTo
    FROM fin.GlAccountRoleAssignment AS r
    WHERE r.AccountId = @Id AND (r.CompanyId IS NULL OR r.CompanyId = @CompanyId)
    ORDER BY r.Role, r.ValidFrom;

    SELECT h.Name, h.ValidFrom, h.ValidTo, u.DisplayName AS ChangedByName
    FROM fin.GlAccountNameHistory AS h
    LEFT JOIN core.[User] AS u ON u.Id = h.ChangedBy
    WHERE h.AccountId = @Id
    ORDER BY h.ValidFrom DESC;
END;
