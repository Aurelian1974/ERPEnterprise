CREATE OR ALTER PROCEDURE fin.usp_ChartOfAccounts_List
AS
BEGIN
    -- Planurile de conturi (C1 3.1): statutarul primul, apoi alternativele, cu numarul de conturi.
    SET NOCOUNT ON;

    SELECT c.Id, c.Code, c.Name, c.Kind, c.Purpose, c.LanguageCode, c.RequireCompleteMapping, c.IsActive,
           (SELECT COUNT(*) FROM fin.GlAccount AS a WHERE a.ChartId = c.Id) AS AccountCount
    FROM fin.ChartOfAccounts AS c
    ORDER BY c.Kind, c.Code;
END;
