CREATE OR ALTER PROCEDURE fin.usp_PeriodCloseStep_Active
AS
BEGIN
    -- Codurile pasilor activi ai listei de verificari (C3 3.3), in ordinea listei.
    SET NOCOUNT ON;
    SELECT Code FROM fin.PeriodCloseStepDefinition WHERE IsActive = 1 ORDER BY SortOrder;
END;
