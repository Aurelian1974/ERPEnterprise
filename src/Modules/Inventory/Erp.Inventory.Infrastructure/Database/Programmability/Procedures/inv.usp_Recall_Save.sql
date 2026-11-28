CREATE OR ALTER PROCEDURE inv.usp_Recall_Save
    @Id                int = NULL,
    @CompanyId         int,
    @DocumentDate      date,
    @SourceKind        tinyint,
    @Reason            nvarchar(1000),
    @SupplierPartnerId int = NULL,
    @SupplierLotCode   nvarchar(40) = NULL,
    @LotIds            inv.IdList READONLY,
    @RowVersion        binary(8) = NULL,
    @UserId            int
AS
BEGIN
    -- Ciorna retragerii (02.05, S4 3.6): loturile firmei date direct si, cu lotul furnizorului, toate loturile firmei cu acelasi cod de la acelasi
    -- furnizor. Loturile se inlocuiesc.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Lots TABLE (LotId int PRIMARY KEY);
    INSERT INTO @Lots SELECT x.Id FROM @LotIds AS i JOIN inv.Lot AS x ON x.Id = i.Id AND x.CompanyId = @CompanyId AND x.IsTechnical = 0;
    INSERT INTO @Lots
    SELECT x.Id FROM inv.Lot AS x
    WHERE @SupplierLotCode IS NOT NULL AND x.CompanyId = @CompanyId AND x.SupplierLotCode = @SupplierLotCode
      AND (@SupplierPartnerId IS NULL OR x.SupplierPartnerId = @SupplierPartnerId) AND x.Id NOT IN (SELECT LotId FROM @Lots);
    IF NOT EXISTS (SELECT 1 FROM @Lots)
        THROW 54454, N'Retragerea nu găsește niciun lot al firmei.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO inv.Recall (CompanyId, DocumentDate, SourceKind, Reason, SupplierPartnerId, SupplierLotCode, CreatedBy)
        VALUES (@CompanyId, @DocumentDate, @SourceKind, @Reason, @SupplierPartnerId, @SupplierLotCode, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @Status tinyint, @Version binary(8);
        SELECT @Status = Status, @Version = RowVersion FROM inv.Recall WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
        IF @Status IS NULL
            THROW 54001, N'Documentul nu există în firma curentă.', 1;
        IF @RowVersion IS NULL OR @Version <> @RowVersion
            THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @Status <> 1
            THROW 54118, N'Documentul nu mai este ciornă.', 1;
        UPDATE inv.Recall SET DocumentDate = @DocumentDate, SourceKind = @SourceKind, Reason = @Reason, SupplierPartnerId = @SupplierPartnerId,
            SupplierLotCode = @SupplierLotCode, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
        DELETE FROM inv.RecallLot WHERE RecallId = @Id;
    END;

    INSERT INTO inv.RecallLot (RecallId, LotId) SELECT @Id, LotId FROM @Lots;
    SELECT Id, RowVersion FROM inv.Recall WHERE Id = @Id;
END;
