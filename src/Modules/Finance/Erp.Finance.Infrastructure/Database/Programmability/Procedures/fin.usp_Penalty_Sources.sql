CREATE OR ALTER PROCEDURE fin.usp_Penalty_Sources
    @CompanyId              int,
    @PartnerId              int = NULL,
    @PeriodFrom             date,
    @PeriodTo               date,
    @ExcludeCalculationId   int = NULL
AS
BEGIN
    -- Datele calculului de penalitati (F3 3.10, 4.7 pasul 1): partidele de client (facturi, note de debit, solduri initiale debitoare) scadente
    -- inainte de sfarsitul perioadei si deschise macar o zi din ea, cu conditiile din profil (fara profil: dobanda legala, fara gratie) si ce s-a
    -- calculat deja pe ele in calculele neanulate (in afara de @ExcludeCalculationId, la recalculare); alocarile active pe ele; cate partide
    -- in litigiu s-au exclus (regula 4).
    SET NOCOUNT ON;

    DECLARE @Items TABLE (OpenItemId bigint PRIMARY KEY);
    INSERT INTO @Items (OpenItemId)
    SELECT o.Id FROM fin.OpenItem AS o
    WHERE o.CompanyId = @CompanyId AND o.Role = 1 AND o.Kind IN (1, 4, 6) AND o.Amount > 0 AND o.IsDisputed = 0 AND o.DueDate < @PeriodTo
      AND o.DocumentDate <= @PeriodTo AND (o.ClosedAt IS NULL OR o.ClosedAt >= @PeriodFrom) AND (@PartnerId IS NULL OR o.PartnerId = @PartnerId);

    SELECT o.Id AS OpenItemId, o.PartnerId, p.Name AS PartnerName, o.DocumentNo, o.DueDate, o.Amount, o.CurrencyCode,
        ISNULL(pf.PenaltyRatePerDay, 0) AS RatePerDay, ISNULL(pf.PenaltyGraceDays, 0) AS GraceDays, ISNULL(pf.PenaltyCapAtPrincipal, 0) AS CapAtPrincipal,
        ISNULL(pf.ApplyFixedRecoveryCost, 0) AS ApplyFixedRecoveryCost, ISNULL(x.Charged, 0) AS AlreadyCharged, x.ChargedThrough,
        CAST(CASE WHEN x.FixedCount > 0 THEN 1 ELSE 0 END AS bit) AS HasFixedCost
    FROM @Items AS i
    JOIN fin.OpenItem AS o ON o.Id = i.OpenItemId
    JOIN core.Partner AS p ON p.Id = o.PartnerId
    LEFT JOIN fin.PartnerFinanceProfile AS pf ON pf.CompanyId = o.CompanyId AND pf.PartnerId = o.PartnerId AND pf.Role = 1
    OUTER APPLY (SELECT SUM(CASE WHEN l.Kind = 1 THEN l.PenaltyAmount END) AS Charged, MAX(CASE WHEN l.Kind = 1 THEN l.ToDate END) AS ChargedThrough,
                        SUM(CASE WHEN l.Kind = 2 THEN 1 ELSE 0 END) AS FixedCount
                 FROM fin.PenaltyCalculationLine AS l JOIN fin.PenaltyCalculation AS c ON c.Id = l.CalculationId
                 WHERE l.OpenItemId = o.Id AND c.Status <> 4 AND (@ExcludeCalculationId IS NULL OR c.Id <> @ExcludeCalculationId)) AS x
    ORDER BY p.Name, o.DueDate, o.Id;

    SELECT a.DebitOpenItemId AS OpenItemId, a.AllocationDate, a.DebitAmount AS Amount
    FROM fin.Allocation AS a JOIN @Items AS i ON i.OpenItemId = a.DebitOpenItemId
    WHERE a.Status = 1
    ORDER BY a.DebitOpenItemId, a.AllocationDate;

    SELECT COUNT(*) FROM fin.OpenItem AS o
    WHERE o.CompanyId = @CompanyId AND o.Role = 1 AND o.Kind IN (1, 4, 6) AND o.Amount > 0 AND o.IsDisputed = 1 AND o.DueDate < @PeriodTo
      AND (o.ClosedAt IS NULL OR o.ClosedAt >= @PeriodFrom) AND (@PartnerId IS NULL OR o.PartnerId = @PartnerId);

    SELECT ValidFrom, Rate FROM fin.ReferenceRate ORDER BY ValidFrom;
END;
