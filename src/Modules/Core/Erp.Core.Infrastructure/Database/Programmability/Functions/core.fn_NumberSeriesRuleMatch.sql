CREATE OR ALTER FUNCTION core.fn_NumberSeriesRuleMatch
(
    @CompanyId      int,
    @DocumentTypeId int,
    @UserId         int = NULL,
    @BranchId       int = NULL,
    @LocationId     int = NULL,
    @PartnerScope   tinyint = NULL
)
RETURNS TABLE
AS
RETURN
    -- Regula de alegere a seriei (02.01, 3.6 punctul 16): prima regula activa (Priority, apoi cea cu mai multe conditii) ale carei
    -- conditii completate se potrivesc toate. Intoarce prefixul seriei tinta.
    SELECT TOP (1) r.Id AS RuleId, r.PrefixKey
    FROM core.NumberSeriesRule AS r
    WHERE r.CompanyId = @CompanyId AND r.DocumentTypeId = @DocumentTypeId AND r.IsActive = 1
      AND (r.UserId IS NULL OR r.UserId = @UserId)
      AND (r.BranchId IS NULL OR r.BranchId = @BranchId)
      AND (r.LocationId IS NULL OR r.LocationId = @LocationId)
      AND (r.PartnerScope IS NULL OR r.PartnerScope = @PartnerScope)
    ORDER BY r.Priority,
             CASE WHEN r.UserId IS NULL THEN 0 ELSE 1 END + CASE WHEN r.BranchId IS NULL THEN 0 ELSE 1 END
             + CASE WHEN r.LocationId IS NULL THEN 0 ELSE 1 END + CASE WHEN r.PartnerScope IS NULL THEN 0 ELSE 1 END DESC,
             r.Id;
