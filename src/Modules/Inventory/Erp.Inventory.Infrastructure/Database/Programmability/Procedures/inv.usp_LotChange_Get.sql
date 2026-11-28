CREATE OR ALTER PROCEDURE inv.usp_LotChange_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Documentul SL (02.05, S4 3.7): antetul si liniile, cu loturile sursa si destinatie.
    SET NOCOUNT ON;

    SELECT d.Id, d.Number, d.WarehouseId, w.Code AS WarehouseCode, d.DepotId, d.DocumentDate, d.Status, d.Notes, d.RowVersion
    FROM inv.LotChange AS d JOIN inv.Warehouse AS w ON w.Id = d.WarehouseId
    WHERE d.Id = @Id AND d.CompanyId = @CompanyId;

    SELECT l.Id AS LineId, l.LineNumber, l.ItemVariantId, v.Code AS VariantCode, l.FromLotId, f.CkuCode AS FromCku, l.ToLotId, t.CkuCode AS ToCku, l.NewLotCode,
        l.NewExpiryDate, l.QtyBase
    FROM inv.LotChangeLine AS l
    JOIN inv.LotChange AS d ON d.Id = l.LotChangeId
    JOIN core.ItemVariant AS v ON v.Id = l.ItemVariantId
    JOIN inv.Lot AS f ON f.Id = l.FromLotId
    LEFT JOIN inv.Lot AS t ON t.Id = l.ToLotId
    WHERE l.LotChangeId = @Id AND d.CompanyId = @CompanyId
    ORDER BY l.LineNumber;
END;
