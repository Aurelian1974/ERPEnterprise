CREATE OR ALTER PROCEDURE inv.usp_WarehouseHandover_Start
    @CompanyId    int,
    @WarehouseId  int,
    @HandoverDate date,
    @ToEmployeeId int,
    @Notes        nvarchar(1000) = NULL,
    @UserId       int
AS
BEGIN
    -- Pornirea predarii-primirii gestiunii (02.05, S1 4.2): cine preda e titularul la data predarii. Cu inventar obligatoriu (setarea firmei
    -- RequireCountOnHandover, doar la bunurile proprii) gestiunea se blocheaza cu motivul „Predare gestiune” pana la finalizare.
    -- Coduri: 54001 gestiunea, 54018 angajatul, 54019 alta predare deschisa.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @IsOwnGoods bit, @Kind tinyint;
    SELECT @IsOwnGoods = IsOwnGoods, @Kind = Kind FROM inv.Warehouse WITH (UPDLOCK, ROWLOCK) WHERE Id = @WarehouseId AND CompanyId = @CompanyId;
    IF @Kind IS NULL
        THROW 54001, N'Gestiunea nu există în firma curentă.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.Employee WHERE Id = @ToEmployeeId AND CompanyId = @CompanyId AND IsActive = 1)
        THROW 54018, N'Gestionarul care primește trebuie să fie un angajat activ al firmei.', 1;
    IF EXISTS (SELECT 1 FROM inv.WarehouseHandover WITH (UPDLOCK, HOLDLOCK) WHERE WarehouseId = @WarehouseId AND Status IN (1, 2))
        THROW 54019, N'Gestiunea are deja o predare în curs.', 1;

    DECLARE @FromEmployeeId int = (SELECT TOP (1) EmployeeId FROM inv.WarehouseKeeper
                                   WHERE WarehouseId = @WarehouseId AND Role = 1 AND ValidFrom <= @HandoverDate
                                     AND (ValidTo IS NULL OR ValidTo >= @HandoverDate)
                                   ORDER BY ValidFrom DESC);
    IF @FromEmployeeId = @ToEmployeeId
        THROW 54018, N'Gestionarul care primește este deja titularul gestiunii.', 1;

    DECLARE @RequiresCount bit = CASE WHEN @IsOwnGoods = 1 AND @Kind <> 3
        AND ISNULL((SELECT RequireCountOnHandover FROM inv.StockSettings WHERE CompanyId = @CompanyId), 1) = 1 THEN 1 ELSE 0 END;

    INSERT INTO inv.WarehouseHandover (WarehouseId, HandoverDate, FromEmployeeId, ToEmployeeId, RequiresCount, Status, Notes, CreatedBy)
    VALUES (@WarehouseId, @HandoverDate, @FromEmployeeId, @ToEmployeeId, @RequiresCount, CASE WHEN @RequiresCount = 1 THEN 2 ELSE 1 END, @Notes, @UserId);
    DECLARE @Id int = SCOPE_IDENTITY();

    IF @RequiresCount = 1
        UPDATE inv.Warehouse
        SET IsBlocked = 1, BlockReason = N'Predare gestiune', BlockedAt = SYSUTCDATETIME(), BlockedBy = @UserId, ModifiedAt = SYSUTCDATETIME(),
            ModifiedBy = @UserId
        WHERE Id = @WarehouseId;

    SELECT Id, RowVersion FROM inv.WarehouseHandover WHERE Id = @Id;
END;
