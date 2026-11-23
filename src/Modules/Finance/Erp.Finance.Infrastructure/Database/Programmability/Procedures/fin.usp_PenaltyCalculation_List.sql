CREATE OR ALTER PROCEDURE fin.usp_PenaltyCalculation_List
    @CompanyId int,
    @Year      int = NULL,
    @Status    tinyint = NULL
AS
BEGIN
    -- Calculele de penalitati ale firmei (cele mai noi intai) si totalurile lor pe moneda (macheta f3-08).
    SET NOCOUNT ON;

    DECLARE @List TABLE (Id int PRIMARY KEY);
    INSERT INTO @List (Id)
    SELECT TOP (200) Id FROM fin.PenaltyCalculation
    WHERE CompanyId = @CompanyId AND (@Year IS NULL OR YEAR(CalculationDate) = @Year) AND (@Status IS NULL OR Status = @Status)
    ORDER BY Id DESC;

    SELECT c.Id, c.Number, c.CalculationDate, c.PeriodFrom, c.PeriodTo, c.PartnerId, p.Name AS PartnerName, c.Status,
        (SELECT COUNT(DISTINCT l.PartnerId) FROM fin.PenaltyCalculationLine AS l WHERE l.CalculationId = c.Id) AS PartnerCount
    FROM @List AS x
    JOIN fin.PenaltyCalculation AS c ON c.Id = x.Id
    LEFT JOIN core.Partner AS p ON p.Id = c.PartnerId
    ORDER BY c.Id DESC;

    SELECT l.CalculationId, l.CurrencyCode, SUM(l.PenaltyAmount) AS Total
    FROM fin.PenaltyCalculationLine AS l JOIN @List AS x ON x.Id = l.CalculationId
    GROUP BY l.CalculationId, l.CurrencyCode
    ORDER BY l.CalculationId, CASE WHEN l.CurrencyCode = 'RON' THEN 0 ELSE 1 END, l.CurrencyCode;
END;
