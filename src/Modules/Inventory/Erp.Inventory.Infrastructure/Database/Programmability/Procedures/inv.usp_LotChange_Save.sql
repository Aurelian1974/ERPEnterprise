CREATE OR ALTER PROCEDURE inv.usp_LotChange_Save
    @Id           int = NULL,
    @CompanyId    int,
    @WarehouseId  int,
    @DepotId      int = NULL,
    @DocumentDate date,
    @Notes        nvarchar(1000) = NULL,
    @Lines        inv.tt_LotChangeLine READONLY,
    @RowVersion   binary(8) = NULL,
    @UserId       int
AS
BEGIN
    -- Ciorna documentului SL (02.05, S4 3.7): pe o gestiune, cantitatea mutata de pe un lot pe altul al aceleiasi variante (54465), existent sau
    -- nou (cod si termen). Liniile se inlocuiesc.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.Warehouse WHERE Id = @WarehouseId AND CompanyId = @CompanyId)
        THROW 54001, N'Gestiunea nu există în firma curentă.', 1;
    IF NOT EXISTS (SELECT 1 FROM @Lines)
        THROW 54018, N'Documentul trebuie să aibă cel puțin o linie.', 1;
    IF EXISTS (SELECT 1 FROM @Lines AS l
               LEFT JOIN inv.Lot AS f ON f.Id = l.FromLotId AND f.CompanyId = @CompanyId
               LEFT JOIN inv.Lot AS t ON t.Id = l.ToLotId AND t.CompanyId = @CompanyId
               WHERE f.Id IS NULL OR f.ItemVariantId <> l.ItemVariantId OR (l.ToLotId IS NOT NULL AND (t.Id IS NULL OR t.ItemVariantId <> l.ItemVariantId))
                  OR l.ToLotId = l.FromLotId)
        THROW 54465, N'Schimbarea lotului se face între loturi diferite ale aceleiași variante.', 1;
    IF EXISTS (SELECT 1 FROM @Lines WHERE QtyBase <= 0 OR (ToLotId IS NULL AND NULLIF(LTRIM(RTRIM(NewLotCode)), N'') IS NULL))
        THROW 54018, N'Fiecare linie are cantitatea și lotul de destinație (existent sau nou).', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO inv.LotChange (CompanyId, WarehouseId, DepotId, DocumentDate, Notes, CreatedBy)
        VALUES (@CompanyId, @WarehouseId, @DepotId, @DocumentDate, @Notes, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @Status tinyint, @Version binary(8);
        SELECT @Status = Status, @Version = RowVersion FROM inv.LotChange WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
        IF @Status IS NULL
            THROW 54001, N'Documentul nu există în firma curentă.', 1;
        IF @RowVersion IS NULL OR @Version <> @RowVersion
            THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @Status <> 1
            THROW 54118, N'Documentul nu mai este ciornă.', 1;
        UPDATE inv.LotChange SET WarehouseId = @WarehouseId, DepotId = @DepotId, DocumentDate = @DocumentDate, Notes = @Notes,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
        DELETE FROM inv.LotChangeLine WHERE LotChangeId = @Id;
    END;

    INSERT INTO inv.LotChangeLine (LotChangeId, LineNumber, ItemVariantId, FromLotId, ToLotId, NewLotCode, NewExpiryDate, QtyBase)
    SELECT @Id, LineNumber, ItemVariantId, FromLotId, ToLotId, NULLIF(LTRIM(RTRIM(NewLotCode)), N''), NewExpiryDate, QtyBase FROM @Lines;

    SELECT Id, RowVersion FROM inv.LotChange WHERE Id = @Id;
END;
