CREATE OR ALTER PROCEDURE fin.usp_AltMapping_Apply
    @EntryId      bigint = NULL,
    @ChartId      int = NULL,
    @CompanyId    int = NULL,
    @PeriodYear   smallint = NULL,
    @PeriodMonth  tinyint = NULL,
    @LineCount    int = NULL OUTPUT,
    @UnmappedCount int = NULL OUTPUT
AS
BEGIN
    -- Maparea liniilor de nota in planurile alternative (C1 4.3, 4.4), pe set: o nota (la validare, in tranzactia ei) sau o luna a unei firme
    -- (recalcularea, pentru un plan). Doar planurile alternative active ale firmei notei; regula din fin.ufn_AltMapping_FindRule (aceeasi ca la
    -- simulator, C1-A9); linia fara regula primeste un rand nemapat (TargetAccountId NULL), ca totalurile sa ramana egale cu planul romanesc
    -- (C1-A7). Tinta procentuala: suma rotunjita pe fiecare tinta, ultima ia restul. Randurile vechi ale acelorasi linii si planuri se rescriu.
    -- Intoarce (OUTPUT) numarul de linii si de linii nemapate.
    SET NOCOUNT ON;

    DECLARE @Lines TABLE (LineId bigint NOT NULL, ChartId int NOT NULL, CompanyId int, PeriodYear smallint, PeriodMonth tinyint, AccountingDate date,
        AccountCode nvarchar(20), Side tinyint, AmountRon decimal(19,2), PartnerId int, PartnerKind tinyint, PartnerGroupId int, CostCenterId int,
        ProjectId int, WarehouseId int, SiteId int, ItemGroupId int, CurrencyCode char(3), PRIMARY KEY (LineId, ChartId));

    INSERT INTO @Lines
    SELECT l.Id, c.Id, l.CompanyId, l.PeriodYear, l.PeriodMonth, e.AccountingDate, a.Code, l.Side, l.AmountRon, l.PartnerId, p.PartnerType, p.PartnerGroupId,
        l.CostCenterId, l.ProjectId, l.WarehouseId, l.SiteId, i.ItemGroupId, l.CurrencyCode
    FROM fin.JournalLine AS l
    JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
    JOIN fin.GlAccount AS a ON a.Id = l.AccountId
    JOIN fin.ChartOfAccountsCompany AS cc ON cc.CompanyId = l.CompanyId
    JOIN fin.ChartOfAccounts AS c ON c.Id = cc.ChartId AND c.Kind = 2 AND c.IsActive = 1
    LEFT JOIN core.Partner AS p ON p.Id = l.PartnerId
    LEFT JOIN core.Item AS i ON i.Id = l.ItemId
    WHERE e.Status >= 2
      AND ((@EntryId IS NOT NULL AND l.EntryId = @EntryId)
           OR (@EntryId IS NULL AND l.CompanyId = @CompanyId AND l.PeriodYear = @PeriodYear AND l.PeriodMonth = @PeriodMonth))
      AND (@ChartId IS NULL OR c.Id = @ChartId);

    DELETE m FROM fin.JournalLineAltAccount AS m JOIN @Lines AS x ON x.LineId = m.JournalLineId AND x.ChartId = m.ChartId;

    INSERT INTO fin.JournalLineAltAccount (JournalLineId, ChartId, CompanyId, PeriodYear, PeriodMonth, Side, TargetAccountId, RuleId, PercentShare, AmountRon)
    SELECT s.LineId, s.ChartId, s.CompanyId, s.PeriodYear, s.PeriodMonth, s.Side, s.TargetAccountId, s.RuleId, s.PercentShare,
        CASE WHEN s.TargetAccountId IS NULL THEN s.AmountRon
             WHEN s.LastTarget = 1 THEN s.AmountRon - (SUM(s.Rounded) OVER (PARTITION BY s.LineId, s.ChartId) - s.Rounded)
             ELSE s.Rounded END
    FROM (
        SELECT x.LineId, x.ChartId, x.CompanyId, x.PeriodYear, x.PeriodMonth, x.Side, x.AmountRon, f.RuleId, t.TargetAccountId, t.PercentShare,
            ROUND(x.AmountRon * ISNULL(t.PercentShare, 100) / 100, 2) AS Rounded,
            ROW_NUMBER() OVER (PARTITION BY x.LineId, x.ChartId ORDER BY t.SortOrder DESC) AS LastTarget
        FROM @Lines AS x
        OUTER APPLY fin.ufn_AltMapping_FindRule(x.ChartId, x.CompanyId, x.AccountCode, x.Side, x.PartnerKind, x.PartnerGroupId, x.PartnerId, x.CostCenterId,
            x.ProjectId, x.WarehouseId, x.SiteId, x.ItemGroupId, x.CurrencyCode, x.AccountingDate) AS f
        LEFT JOIN fin.AccountMappingTarget AS t ON t.RuleId = f.RuleId) AS s;

    SET @LineCount = (SELECT COUNT(*) FROM @Lines);
    SET @UnmappedCount = (SELECT COUNT(DISTINCT m.JournalLineId) FROM fin.JournalLineAltAccount AS m
                          JOIN @Lines AS x ON x.LineId = m.JournalLineId AND x.ChartId = m.ChartId WHERE m.TargetAccountId IS NULL);
END;
