CREATE OR ALTER PROCEDURE fin.usp_TaxCalendar_List
    @CompanyId  int,
    @From       date,
    @To         date
AS
BEGIN
    -- Calendarul fiscal (macheta c4-01): termenele din interval, cu declaratia initiala a perioadei, starea ei si marcajul „De verificat”.
    SET NOCOUNT ON;
    SELECT c.FormCode, f.Name AS FormName, c.PeriodFrom, c.PeriodTo, c.DueDate, r.Id AS ReturnId, r.Status, r.NeedsReview, r.AmountDue
    FROM fin.TaxCalendarItem AS c
    JOIN fin.TaxForm AS f ON f.Code = c.FormCode
    LEFT JOIN fin.TaxReturn AS r ON r.CompanyId = c.CompanyId AND r.FormCode = c.FormCode AND r.PeriodFrom = c.PeriodFrom AND r.Kind = 1 AND r.Status <> 5
    WHERE c.CompanyId = @CompanyId AND c.DueDate BETWEEN @From AND @To
    ORDER BY c.DueDate, c.FormCode;
END;
