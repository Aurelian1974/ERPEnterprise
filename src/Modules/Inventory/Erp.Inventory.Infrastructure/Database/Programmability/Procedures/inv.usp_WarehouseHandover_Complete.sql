CREATE OR ALTER PROCEDURE inv.usp_WarehouseHandover_Complete
    @CompanyId  int,
    @Id         int,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Finalizarea predarii (02.05, S1 4.2 pasul 4): fara inventarul finalizat, cand e obligatoriu, refuzata (54013, S1-A7; inventarul vine cu S6).
    -- Titularul vechi se inchide cu ziua dinaintea predarii, cel nou incepe la data predarii (perioade continue), gestiunea blocata de predare
    -- se deblocheaza. Intoarce datele evenimentului GestionarSchimbat. Coduri: 54001, 54014, 54020 predarea nu e deschisa.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @WarehouseId int, @Status tinyint, @RequiresCount bit, @InventoryCountId int, @HandoverDate date, @FromEmployeeId int, @ToEmployeeId int;
    SELECT @WarehouseId = h.WarehouseId, @Status = h.Status, @RequiresCount = h.RequiresCount, @InventoryCountId = h.InventoryCountId,
        @HandoverDate = h.HandoverDate, @FromEmployeeId = h.FromEmployeeId, @ToEmployeeId = h.ToEmployeeId
    FROM inv.WarehouseHandover AS h WITH (UPDLOCK, ROWLOCK)
    JOIN inv.Warehouse AS w ON w.Id = h.WarehouseId
    WHERE h.Id = @Id AND w.CompanyId = @CompanyId;
    IF @WarehouseId IS NULL
        THROW 54001, N'Predarea nu există în firma curentă.', 1;
    IF @Status NOT IN (1, 2)
        THROW 54020, N'Predarea nu mai este în curs.', 1;
    -- Inventarul de predare (S6) nu exista inca: o predare care il cere nu se poate finaliza pana la legarea lui.
    IF @RequiresCount = 1 AND @InventoryCountId IS NULL
        THROW 54013, N'Predarea cere inventarul de predare finalizat.', 1;
    IF EXISTS (SELECT 1 FROM inv.WarehouseKeeper WHERE WarehouseId = @WarehouseId AND Role = 1 AND ValidFrom >= @HandoverDate)
        THROW 54009, N'Există un titular care începe la data predării sau după ea.', 1;

    UPDATE inv.WarehouseHandover
    SET Status = 3, CompletedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
        THROW 54014, N'Predarea a fost modificată de alt utilizator; reîncărcați datele.', 1;

    UPDATE inv.WarehouseKeeper
    SET ValidTo = DATEADD(DAY, -1, @HandoverDate), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE WarehouseId = @WarehouseId AND Role = 1 AND ValidFrom < @HandoverDate AND (ValidTo IS NULL OR ValidTo >= @HandoverDate);

    INSERT INTO inv.WarehouseKeeper (WarehouseId, EmployeeId, Role, ValidFrom, HandoverId, CreatedBy)
    VALUES (@WarehouseId, @ToEmployeeId, 1, @HandoverDate, @Id, @UserId);

    UPDATE inv.Warehouse
    SET IsBlocked = 0, BlockReason = NULL, BlockedAt = NULL, BlockedBy = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @WarehouseId AND IsBlocked = 1 AND BlockReason = N'Predare gestiune';

    SELECT Id, RowVersion, WarehouseId, HandoverDate, FromEmployeeId, ToEmployeeId, InventoryCountId FROM inv.WarehouseHandover WHERE Id = @Id;
END;
