CREATE OR ALTER PROCEDURE inv.usp_WarehouseKeeper_List
    @CompanyId   int,
    @WarehouseId int
AS
BEGIN
    -- Tab-ul Gestionari al gestiunii (02.05, S1 11; macheta s1-02): gestionarii cu perioadele lor, apoi predarile, cele mai noi intai.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.Warehouse WHERE Id = @WarehouseId AND CompanyId = @CompanyId)
        THROW 54001, N'Gestiunea nu există în firma curentă.', 1;

    SELECT k.Id, k.EmployeeId, CONCAT(e.FirstName, N' ', e.LastName) AS EmployeeName, k.Role, k.ValidFrom, k.ValidTo, k.GuaranteeNote, k.HandoverId,
        e.LeaveDate, k.RowVersion
    FROM inv.WarehouseKeeper AS k
    JOIN core.Employee AS e ON e.Id = k.EmployeeId
    WHERE k.WarehouseId = @WarehouseId
    ORDER BY k.Role, k.ValidFrom DESC;

    SELECT h.Id, h.HandoverDate, h.FromEmployeeId, CONCAT(f.FirstName, N' ', f.LastName) AS FromEmployeeName, h.ToEmployeeId,
        CONCAT(t.FirstName, N' ', t.LastName) AS ToEmployeeName, h.RequiresCount, h.InventoryCountId, h.Status, h.Notes, h.CompletedAt, h.RowVersion
    FROM inv.WarehouseHandover AS h
    LEFT JOIN core.Employee AS f ON f.Id = h.FromEmployeeId
    JOIN core.Employee AS t ON t.Id = h.ToEmployeeId
    WHERE h.WarehouseId = @WarehouseId
    ORDER BY h.HandoverDate DESC, h.Id DESC;

    SELECT Id, CONCAT(FirstName, N' ', LastName) AS Name, LeaveDate
    FROM core.Employee
    WHERE CompanyId = @CompanyId AND IsActive = 1
    ORDER BY LastName, FirstName;
END;
