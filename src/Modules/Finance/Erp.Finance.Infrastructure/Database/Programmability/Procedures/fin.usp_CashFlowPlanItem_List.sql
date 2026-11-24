CREATE OR ALTER PROCEDURE fin.usp_CashFlowPlanItem_List
    @CompanyId      int,
    @IncludeInactive bit = 0
AS
BEGIN
    -- Sumele planificate ale firmei (F5 3.5).
    SET NOCOUNT ON;

    SELECT p.Id, p.CategoryId, c.Code AS CategoryCode, c.Name AS CategoryName, c.Direction, p.Description, p.CurrencyCode, p.Amount,
           p.TreasuryAccountId, a.Code AS TreasuryAccountCode, p.FirstDate, p.Recurrence, p.RecurrenceDay, p.EndDate, p.IsActive, p.Probability,
           p.RowVersion
    FROM fin.CashFlowPlanItem AS p
    JOIN fin.CashFlowCategory AS c ON c.Id = p.CategoryId
    LEFT JOIN fin.TreasuryAccount AS a ON a.Id = p.TreasuryAccountId
    WHERE p.CompanyId = @CompanyId AND (@IncludeInactive = 1 OR p.IsActive = 1)
    ORDER BY c.SortOrder, p.FirstDate, p.Id;
END;
