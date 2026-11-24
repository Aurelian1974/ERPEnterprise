CREATE OR ALTER PROCEDURE fin.usp_CashFlowCategory_List
AS
BEGIN
    -- Categoriile de cash-flow (F5 3.5), in ordinea de afisare.
    SET NOCOUNT ON;

    SELECT Id, Code, Name, Direction, SortOrder, ParentId, IsSystem, IsActive
    FROM fin.CashFlowCategory
    ORDER BY SortOrder, Code;
END;
