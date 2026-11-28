CREATE OR ALTER PROCEDURE inv.usp_WarehouseHandover_Cancel
    @CompanyId  int,
    @Id         int,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Anularea unei predari in curs (02.05, S1 3.2): gestionarii raman neschimbati, iar blocarea pusa de predare se ridica. Coduri: 54001, 54014,
    -- 54020 predarea nu e deschisa.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @WarehouseId int, @Status tinyint;
    SELECT @WarehouseId = h.WarehouseId, @Status = h.Status
    FROM inv.WarehouseHandover AS h WITH (UPDLOCK, ROWLOCK)
    JOIN inv.Warehouse AS w ON w.Id = h.WarehouseId
    WHERE h.Id = @Id AND w.CompanyId = @CompanyId;
    IF @WarehouseId IS NULL
        THROW 54001, N'Predarea nu există în firma curentă.', 1;
    IF @Status NOT IN (1, 2)
        THROW 54020, N'Predarea nu mai este în curs.', 1;

    UPDATE inv.WarehouseHandover
    SET Status = 4, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
        THROW 54014, N'Predarea a fost modificată de alt utilizator; reîncărcați datele.', 1;

    UPDATE inv.Warehouse
    SET IsBlocked = 0, BlockReason = NULL, BlockedAt = NULL, BlockedBy = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @WarehouseId AND IsBlocked = 1 AND BlockReason = N'Predare gestiune';

    SELECT Id, RowVersion FROM inv.WarehouseHandover WHERE Id = @Id;
END;
