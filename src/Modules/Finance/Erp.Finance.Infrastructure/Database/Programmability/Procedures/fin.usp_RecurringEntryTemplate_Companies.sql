CREATE OR ALTER PROCEDURE fin.usp_RecurringEntryTemplate_Companies
AS
BEGIN
    -- Firmele cu sabloane recurente active (jobul de generare, C2 3.8).
    SET NOCOUNT ON;
    SELECT DISTINCT CompanyId FROM fin.RecurringEntryTemplate WHERE IsActive = 1;
END;
