CREATE OR ALTER PROCEDURE inv.usp_WarehouseKeeper_Save
    @CompanyId     int,
    @Id            int = NULL,
    @WarehouseId   int,
    @EmployeeId    int,
    @Role          tinyint,
    @ValidFrom     date,
    @ValidTo       date = NULL,
    @GuaranteeNote nvarchar(500) = NULL,
    @RowVersion    binary(8) = NULL,
    @UserId        int
AS
BEGIN
    -- Gestionarul titular sau inlocuitor al unei gestiuni (02.05, S1 3.2), cu perioada lui. Angajatul e al firmei (54018); perioadele titularilor
    -- nu se suprapun (54009, S1-A6). Gestiunea altei firme: 54001; versiune diferita: 54014.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.Warehouse WITH (UPDLOCK, HOLDLOCK) WHERE Id = @WarehouseId AND CompanyId = @CompanyId)
        THROW 54001, N'Gestiunea nu există în firma curentă.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.Employee WHERE Id = @EmployeeId AND CompanyId = @CompanyId)
        THROW 54018, N'Gestionarul trebuie să fie un angajat al firmei.', 1;
    IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM inv.WarehouseKeeper WHERE Id = @Id AND WarehouseId = @WarehouseId)
        THROW 54001, N'Gestionarul nu există pe această gestiune.', 1;
    IF @Role = 1 AND EXISTS (SELECT 1 FROM inv.WarehouseKeeper
                             WHERE WarehouseId = @WarehouseId AND Role = 1 AND (@Id IS NULL OR Id <> @Id)
                               AND ValidFrom <= ISNULL(@ValidTo, '99991231') AND ISNULL(ValidTo, '99991231') >= @ValidFrom)
        THROW 54009, N'Gestiunea are deja un gestionar titular în această perioadă; schimbarea titularului se face prin predare-primire.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO inv.WarehouseKeeper (WarehouseId, EmployeeId, Role, ValidFrom, ValidTo, GuaranteeNote, CreatedBy)
        VALUES (@WarehouseId, @EmployeeId, @Role, @ValidFrom, @ValidTo, @GuaranteeNote, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE inv.WarehouseKeeper
        SET EmployeeId = @EmployeeId, Role = @Role, ValidFrom = @ValidFrom, ValidTo = @ValidTo, GuaranteeNote = @GuaranteeNote,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 54014, N'Gestionarul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    END;

    SELECT Id, RowVersion FROM inv.WarehouseKeeper WHERE Id = @Id;
END;
