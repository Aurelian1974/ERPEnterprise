CREATE OR ALTER PROCEDURE fin.usp_PaymentBatch_Save
    @Id                int = NULL,
    @CompanyId         int,
    @TreasuryAccountId int,
    @Orders            fin.IdVersionList READONLY,
    @RowVersion        binary(8) = NULL,
    @UserId            int
AS
BEGIN
    -- Un lot deschis de ordine de plata (02.02, F2 3.6): lista inlocuieste OP-urile lotului (cele scoase raman validate si libere). Un lot
    -- exportat sau anulat nu se modifica (51222). OP-urile sunt ale contului lotului, validate, nestornate, neconfirmate, din ecranul Ordine
    -- de plata (cele din extras sunt deja executate) si nu sunt in alt lot (51222).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount WHERE Id = @TreasuryAccountId AND CompanyId = @CompanyId AND Kind IN (1, 3))
        THROW 51023, N'Contul bancar nu există în firma curentă.', 1;

    IF @Id IS NOT NULL
    BEGIN
        DECLARE @Status tinyint, @BatchCompanyId int, @Version binary(8), @BatchAccountId int;
        SELECT @Status = Status, @BatchCompanyId = CompanyId, @Version = RowVersion, @BatchAccountId = TreasuryAccountId
        FROM fin.PaymentBatch WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @Status IS NULL OR @BatchCompanyId <> @CompanyId
            THROW 51226, N'Lotul de plăți nu există.', 1;
        IF @Version <> @RowVersion
            THROW 51202, N'Lotul de plăți a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @Status <> 1
            THROW 51222, N'Doar un lot deschis se modifică.', 1;
        IF @BatchAccountId <> @TreasuryAccountId
            THROW 51021, N'Contul lotului nu se schimbă.', 1;
    END;

    IF (SELECT COUNT(*) FROM fin.PaymentOrder WITH (UPDLOCK) WHERE Id IN (SELECT Id FROM @Orders) AND CompanyId = @CompanyId) <> (SELECT COUNT(*) FROM @Orders)
        THROW 51225, N'Unul sau mai multe ordine de plată nu există.', 1;
    IF EXISTS (SELECT 1 FROM fin.PaymentOrder AS o JOIN @Orders AS p ON p.Id = o.Id
               WHERE o.TreasuryAccountId <> @TreasuryAccountId OR o.Status <> 2 OR o.ReversalOfId IS NOT NULL OR o.ConfirmationStatus <> 1 OR o.Origin = 2
                  OR (o.PaymentBatchId IS NOT NULL AND (@Id IS NULL OR o.PaymentBatchId <> @Id)))
        THROW 51222, N'În lot intră doar OP-uri validate și neconfirmate ale contului, care nu sunt în alt lot.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.PaymentBatch (CompanyId, TreasuryAccountId, CreatedBy) VALUES (@CompanyId, @TreasuryAccountId, @UserId);
        SET @Id = SCOPE_IDENTITY();
        UPDATE fin.PaymentBatch SET Number = CONCAT(N'LOT-', @Id) WHERE Id = @Id;
    END;

    UPDATE fin.PaymentOrder SET PaymentBatchId = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE PaymentBatchId = @Id AND Id NOT IN (SELECT Id FROM @Orders);
    UPDATE fin.PaymentOrder SET PaymentBatchId = @Id, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id IN (SELECT Id FROM @Orders) AND (PaymentBatchId IS NULL OR PaymentBatchId <> @Id);

    UPDATE b
    SET OrderCount = t.OrderCount, TotalAmount = t.TotalAmount, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.PaymentBatch AS b
    CROSS APPLY (SELECT COUNT(*) AS OrderCount, ISNULL(SUM(Amount), 0) AS TotalAmount FROM fin.PaymentOrder WHERE PaymentBatchId = b.Id) AS t
    WHERE b.Id = @Id;

    SELECT Id, RowVersion FROM fin.PaymentBatch WHERE Id = @Id;
END;
