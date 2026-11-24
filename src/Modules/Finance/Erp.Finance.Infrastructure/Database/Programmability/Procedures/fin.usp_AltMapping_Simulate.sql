CREATE OR ALTER PROCEDURE fin.usp_AltMapping_Simulate
    @ChartId       int,
    @CompanyId     int,
    @AccountCode   nvarchar(20),
    @Side          tinyint,
    @PartnerId     int = NULL,
    @CostCenterId  int = NULL,
    @ProjectId     int = NULL,
    @WarehouseId   int = NULL,
    @SiteId        int = NULL,
    @ItemGroupId   int = NULL,
    @CurrencyCode  char(3) = NULL,
    @OnDate        date
AS
BEGIN
    -- Simulatorul (C1 4.5): regula aleasa si tintele pentru un cont romanesc si un set de atribute, fara sa scrie nimic; tipul si grupa
    -- partenerului vin din fisa lui. Aceeasi potrivire ca la validarea notei (fin.ufn_AltMapping_FindRule, C1-A9).
    SET NOCOUNT ON;

    DECLARE @Kind tinyint, @Group int;
    SELECT @Kind = PartnerType, @Group = PartnerGroupId FROM core.Partner WHERE Id = @PartnerId;

    SELECT r.Id AS RuleId, r.Priority, r.Note, t.TargetAccountId, a.Code, a.Name, t.PercentShare
    FROM fin.ufn_AltMapping_FindRule(@ChartId, @CompanyId, @AccountCode, @Side, @Kind, @Group, @PartnerId, @CostCenterId, @ProjectId, @WarehouseId,
        @SiteId, @ItemGroupId, @CurrencyCode, @OnDate) AS f
    JOIN fin.AccountMappingRule AS r ON r.Id = f.RuleId
    JOIN fin.AccountMappingTarget AS t ON t.RuleId = r.Id
    JOIN fin.GlAccount AS a ON a.Id = t.TargetAccountId
    ORDER BY t.SortOrder;
END;
