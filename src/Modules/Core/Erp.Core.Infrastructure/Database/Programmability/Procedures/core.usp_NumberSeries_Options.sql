CREATE OR ALTER PROCEDURE core.usp_NumberSeries_Options
    @CompanyId        int,
    @BranchId         int = NULL,
    @DocumentTypeCode nvarchar(50),
    @DocumentDate     date,
    @UserId           int = NULL,
    @LocationId       int = NULL,
    @PartnerScope     tinyint = NULL
AS
BEGIN
    -- Seriile din care utilizatorul poate alege pe un document (02.01, 3.6 punctul 16): seriile deschise ale anului, ale firmei sau ale
    -- punctului de lucru, cu numarul urmator; ProposedSeriesId = seria din prima regula potrivita, altfel cea implicita. Doar citire:
    -- numarul se aloca la validare (usp_NumberSeries_Next).
    SET NOCOUNT ON;

    DECLARE @Year smallint = YEAR(@DocumentDate);
    DECLARE @DocumentTypeId int = (SELECT Id FROM core.DocumentType WHERE Code = @DocumentTypeCode);
    DECLARE @TargetPrefix nvarchar(20) = (SELECT m.PrefixKey FROM core.fn_NumberSeriesRuleMatch(@CompanyId, @DocumentTypeId, @UserId, @BranchId,
        @LocationId, @PartnerScope) AS m);

    SELECT s.Id AS SeriesId, s.Prefix, s.[Year], s.NextNumber, s.Padding, s.IsFiscal, s.IsDefault, s.BranchId,
           CAST(CASE WHEN s.PrefixKey = @TargetPrefix THEN 1 ELSE 0 END AS bit) AS MatchesRule
    FROM core.NumberSeries AS s
    WHERE s.CompanyId = @CompanyId AND s.DocumentTypeId = @DocumentTypeId AND s.[Year] = @Year AND s.ClosedAt IS NULL
      AND (s.BranchId IS NULL OR s.BranchId = @BranchId)
    ORDER BY CASE WHEN s.BranchId = @BranchId THEN 0 ELSE 1 END, s.IsDefault DESC, s.PrefixKey;
END;
