CREATE OR ALTER PROCEDURE inv.usp_WarehouseLookup_Keeper
    @WarehouseId int,
    @OnDate      date
AS
BEGIN
    -- Gestionarul titular al gestiunii la o data (IWarehouseLookup, S1 6), pentru documentele tiparite si notificari; niciun rand fara titular.
    SET NOCOUNT ON;

    SELECT TOP (1) EmployeeId FROM inv.WarehouseKeeper
    WHERE WarehouseId = @WarehouseId AND Role = 1 AND ValidFrom <= @OnDate AND (ValidTo IS NULL OR ValidTo >= @OnDate)
    ORDER BY ValidFrom DESC;
END;
