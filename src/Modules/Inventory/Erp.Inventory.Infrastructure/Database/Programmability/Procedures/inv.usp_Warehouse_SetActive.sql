CREATE OR ALTER PROCEDURE inv.usp_Warehouse_SetActive
    @CompanyId  int,
    @Id         int,
    @IsActive   bit,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Dezactivarea si reactivarea gestiunii (02.05, S1 4.4). Tranzitul nu se dezactiveaza; o gestiune cu stoc, ciorne sau rezervari active nu se
    -- dezactiveaza (54006; ciornele si rezervarile se adauga cu documentele S2 si S5). Gestiune inexistenta: 54001; versiune: 54014.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Kind tinyint = (SELECT Kind FROM inv.Warehouse WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId);
    IF @Kind IS NULL
        THROW 54001, N'Gestiunea nu există în firma curentă.', 1;
    IF @IsActive = 0 AND @Kind = 3
        THROW 54006, N'Gestiunea de tranzit nu se dezactivează.', 1;
    IF @IsActive = 0 AND EXISTS (SELECT 1 FROM inv.Stock WHERE WarehouseId = @Id AND (QtyOnHand <> 0 OR CostOnHand <> 0))
        THROW 54006, N'Gestiunea are stoc; goliți-o (transfer sau ieșire) înainte de dezactivare.', 1;

    UPDATE inv.Warehouse
    SET IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND RowVersion = @RowVersion;

    IF @@ROWCOUNT = 0
        THROW 54014, N'Gestiunea a fost modificată de alt utilizator; reîncărcați datele.', 1;

    SELECT Id, RowVersion, DimensionValueId, IsActive FROM inv.Warehouse WHERE Id = @Id;
END;
