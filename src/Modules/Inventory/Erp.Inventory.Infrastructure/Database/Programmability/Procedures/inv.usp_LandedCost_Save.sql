CREATE OR ALTER PROCEDURE inv.usp_LandedCost_Save
    @Id                int = NULL,
    @CompanyId         int,
    @DocumentDate      date,
    @AccountingDate    date,
    @SourceKind        tinyint,
    @SupplierInvoiceId bigint = NULL,
    @PartnerId         int = NULL,
    @Amount            decimal(19,2),
    @AllocationBasis   tinyint,
    @Notes             nvarchar(1000) = NULL,
    @Lines             inv.tt_LandedCostLine READONLY,
    @RowVersion        binary(8) = NULL,
    @UserId            int
AS
BEGIN
    -- Salvarea documentului CA in ciorna (02.05, S3 3.3), cu repartizarea calculata de apelant (LandedCostAllocator). Liniile se inlocuiesc.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM @Lines)
        THROW 54304, N'Documentul de costuri adăugate trebuie să aibă cel puțin o intrare țintă.', 1;
    IF (SELECT SUM(AllocatedAmount) FROM @Lines) <> @Amount
        THROW 54304, N'Suma repartizată pe linii trebuie să fie egală cu suma documentului.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO inv.LandedCost (CompanyId, DocumentDate, AccountingDate, SourceKind, SupplierInvoiceId, PartnerId, Amount, AllocationBasis, Notes, CreatedBy)
        VALUES (@CompanyId, @DocumentDate, @AccountingDate, @SourceKind, @SupplierInvoiceId, @PartnerId, @Amount, @AllocationBasis, @Notes, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @Status tinyint, @Version binary(8);
        SELECT @Status = Status, @Version = RowVersion FROM inv.LandedCost WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
        IF @Status IS NULL
            THROW 54001, N'Documentul nu există în firma curentă.', 1;
        IF @RowVersion IS NULL OR @Version <> @RowVersion
            THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @Status <> 1
            THROW 54118, N'Documentul nu mai este ciornă.', 1;

        UPDATE inv.LandedCost
        SET DocumentDate = @DocumentDate, AccountingDate = @AccountingDate, SourceKind = @SourceKind, SupplierInvoiceId = @SupplierInvoiceId,
            PartnerId = @PartnerId, Amount = @Amount, AllocationBasis = @AllocationBasis, Notes = @Notes, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
        DELETE FROM inv.LandedCostLine WHERE LandedCostId = @Id;
    END;

    INSERT INTO inv.LandedCostLine (LandedCostId, LineNumber, MovementId, ManualAmount, AllocatedAmount)
    SELECT @Id, l.LineNumber, l.MovementId, l.ManualAmount, l.AllocatedAmount FROM @Lines AS l;

    SELECT Id, RowVersion FROM inv.LandedCost WHERE Id = @Id;
END;
