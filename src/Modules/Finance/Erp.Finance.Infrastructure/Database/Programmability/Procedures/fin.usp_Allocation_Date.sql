CREATE OR ALTER PROCEDURE fin.usp_Allocation_Date
    @CompanyId    int,
    @AllocationId bigint
AS
BEGIN
    -- Data unei alocari active a firmei (F3 3.3), pentru verificarea perioadei inchise inainte de desfacere.
    SET NOCOUNT ON;

    SELECT a.AllocationDate FROM fin.Allocation AS a WHERE a.Id = @AllocationId AND a.CompanyId = @CompanyId AND a.Status = 1;
END;
