CREATE OR ALTER PROCEDURE inv.usp_ItemWarehouseSettings_Delete
    @CompanyId  int,
    @Id         int,
    @RowVersion binary(8)
AS
BEGIN
    -- Stergerea pragurilor pe gestiune (02.05, S1 3.4): se revine la pragurile modelului in gestiune sau ale firmei. Coduri: 54001, 54014.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.ItemWarehouseSettings AS x JOIN inv.Warehouse AS w ON w.Id = x.WarehouseId WHERE x.Id = @Id AND w.CompanyId = @CompanyId)
        THROW 54001, N'Pragurile nu există în firma curentă.', 1;

    DELETE FROM inv.ItemWarehouseSettings WHERE Id = @Id AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
        THROW 54014, N'Pragurile au fost modificate de alt utilizator; reîncărcați datele.', 1;
END;
