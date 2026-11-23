CREATE OR ALTER PROCEDURE fin.usp_PenaltyCalculation_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Antetul si liniile unui calcul de penalitati (macheta f3-08).
    SET NOCOUNT ON;

    SELECT c.Id, c.Number, c.CalculationDate, c.PeriodFrom, c.PeriodTo, c.PartnerId, p.Name AS PartnerName, c.Status, c.DisputedCount, c.ApprovedAt,
        ua.DisplayName AS ApprovedByName, c.CreatedAt, uc.DisplayName AS CreatedByName, c.RowVersion
    FROM fin.PenaltyCalculation AS c
    LEFT JOIN core.Partner AS p ON p.Id = c.PartnerId
    LEFT JOIN core.[User] AS ua ON ua.Id = c.ApprovedBy
    LEFT JOIN core.[User] AS uc ON uc.Id = c.CreatedBy
    WHERE c.Id = @Id AND c.CompanyId = @CompanyId;

    SELECT l.Id, l.LineNumber, l.Kind, l.PartnerId, p.Name AS PartnerName, l.OpenItemId, o.DocumentNo, l.DueDate, l.FromDate, l.ToDate, l.Days, l.BaseAmount,
        l.CurrencyCode, l.RatePerDay, l.AnnualRate, l.PenaltyAmount
    FROM fin.PenaltyCalculationLine AS l
    JOIN fin.PenaltyCalculation AS c ON c.Id = l.CalculationId
    JOIN core.Partner AS p ON p.Id = l.PartnerId
    JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId
    WHERE l.CalculationId = @Id AND c.CompanyId = @CompanyId
    ORDER BY l.LineNumber;
END;
