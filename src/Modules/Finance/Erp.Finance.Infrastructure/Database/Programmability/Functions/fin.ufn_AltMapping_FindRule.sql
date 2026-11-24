CREATE OR ALTER FUNCTION fin.ufn_AltMapping_FindRule (
    @ChartId        int,
    @CompanyId      int,
    @AccountCode    nvarchar(20),
    @Side           tinyint,
    @PartnerKind    tinyint,
    @PartnerGroupId int,
    @PartnerId      int,
    @CostCenterId   int,
    @ProjectId      int,
    @WarehouseId    int,
    @SiteId         int,
    @ItemGroupId    int,
    @CurrencyCode   char(3),
    @OnDate         date)
RETURNS TABLE
AS
RETURN
    -- Regula de mapare a unei linii de nota intr-un plan alternativ (C1 3.5, 4.3): regulile active, in vigoare la data contabila, fara
    -- conditie de sold (aceea se evalueaza in rapoarte), ale caror sursa si conditii se potrivesc, in ordinea prioritatii; prima castiga.
    -- O singura definitie pentru simulator si pentru maparea la validarea notei (C1-A9). Sursa „un cont” cuprinde contul si descendentii
    -- lui (sinteticele de grad inferior prin cifre, analiticele prin punct); intervalul compara codurile normalizate (simbolul sintetic
    -- completat cu zerouri la 4 cifre).
    SELECT TOP (1) r.Id AS RuleId, r.Priority
    FROM fin.AccountMappingRule AS r
    CROSS APPLY (SELECT LEFT(@AccountCode, CHARINDEX(N'.', @AccountCode + N'.') - 1) AS Synthetic) AS c
    CROSS APPLY (SELECT CASE WHEN LEN(c.Synthetic) >= 4 THEN c.Synthetic ELSE LEFT(c.Synthetic + N'0000', 4) END AS Normalized) AS n
    WHERE r.TargetChartId = @ChartId AND r.IsActive = 1 AND r.BalanceSign = 0 AND r.ValidFrom <= @OnDate AND (r.ValidTo IS NULL OR r.ValidTo >= @OnDate)
      AND (r.CompanyId IS NULL OR r.CompanyId = @CompanyId)
      AND (r.Side = 0 OR r.Side = @Side)
      AND (r.PartnerKind IS NULL OR r.PartnerKind = @PartnerKind)
      AND (r.PartnerGroupId IS NULL OR r.PartnerGroupId = @PartnerGroupId)
      AND (r.PartnerId IS NULL OR r.PartnerId = @PartnerId)
      AND (r.ItemGroupId IS NULL OR r.ItemGroupId = @ItemGroupId)
      AND (r.CurrencyCode IS NULL OR r.CurrencyCode = @CurrencyCode)
      AND (r.DimensionCondition = 0
           OR (r.DimensionCondition = 1 AND r.DimensionValueId = CASE r.DimensionType WHEN 'COST_CENTER' THEN @CostCenterId WHEN 'PROJECT' THEN @ProjectId
                                                                 WHEN 'WAREHOUSE' THEN @WarehouseId WHEN 'SITE' THEN @SiteId END)
           OR (r.DimensionCondition = 2 AND CASE r.DimensionType WHEN 'COST_CENTER' THEN @CostCenterId WHEN 'PROJECT' THEN @ProjectId
                                                WHEN 'WAREHOUSE' THEN @WarehouseId WHEN 'SITE' THEN @SiteId END IS NOT NULL)
           OR (r.DimensionCondition = 3 AND CASE r.DimensionType WHEN 'COST_CENTER' THEN @CostCenterId WHEN 'PROJECT' THEN @ProjectId
                                                WHEN 'WAREHOUSE' THEN @WarehouseId WHEN 'SITE' THEN @SiteId END IS NULL))
      AND ((r.SourceKind = 1 AND EXISTS (SELECT 1 FROM fin.GlAccount AS s WHERE s.Id = r.SourceAccountId
                                         AND (@AccountCode = s.Code OR @AccountCode LIKE s.Code + N'.%'
                                              OR (CHARINDEX(N'.', s.Code) = 0 AND @AccountCode LIKE s.Code + N'[0-9]%'))))
           OR (r.SourceKind = 2 AND n.Normalized BETWEEN r.SourceFrom AND r.SourceTo)
           OR (r.SourceKind = 3 AND EXISTS (SELECT 1 FROM fin.AccountMappingSource AS ms JOIN fin.GlAccount AS s ON s.Id = ms.AccountId
                                            WHERE ms.RuleId = r.Id AND (@AccountCode = s.Code OR @AccountCode LIKE s.Code + N'.%'
                                                                       OR (CHARINDEX(N'.', s.Code) = 0 AND @AccountCode LIKE s.Code + N'[0-9]%')))))
    ORDER BY r.Priority;
