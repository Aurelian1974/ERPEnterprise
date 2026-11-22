CREATE OR ALTER PROCEDURE fin.usp_PaymentOrder_Approve
    @CompanyId       int,
    @Orders          fin.IdVersionList READONLY,
    @Approve         bit,
    @RequireApproval bit,
    @UserId          int
AS
BEGIN
    -- Aprobarea (sau retragerea aprobarii) unor OP-uri validate (02.02, F2 3.6 regula 3), pe lot, in ordinea Id-urilor (SP-16). Cu patru
    -- ochi activ, cine a validat un OP nu il aproba (51213). Doar OP-urile validate, nestornate, nerespinse (51212). Retragerea nu se face pe
    -- un OP pus intr-un lot exportat (51217 cand e deja confirmat).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Locked TABLE (Id int PRIMARY KEY, Status tinyint, ConfirmationStatus tinyint, ReversalOfId int, ValidatedBy int, RowVersion binary(8));
    INSERT INTO @Locked (Id, Status, ConfirmationStatus, ReversalOfId, ValidatedBy, RowVersion)
    SELECT o.Id, o.Status, o.ConfirmationStatus, o.ReversalOfId, o.ValidatedBy, o.RowVersion
    FROM fin.PaymentOrder AS o WITH (UPDLOCK, ROWLOCK)
    WHERE o.Id IN (SELECT Id FROM @Orders) AND o.CompanyId = @CompanyId
    ORDER BY o.Id;

    IF (SELECT COUNT(*) FROM @Locked) <> (SELECT COUNT(*) FROM @Orders)
        THROW 51225, N'Unul sau mai multe ordine de plată nu există.', 1;
    IF EXISTS (SELECT 1 FROM @Locked AS k JOIN @Orders AS p ON p.Id = k.Id WHERE p.RowVersion IS NULL OR p.RowVersion <> k.RowVersion)
        THROW 51202, N'Un ordin de plată a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF EXISTS (SELECT 1 FROM @Locked WHERE Status <> 2 OR ReversalOfId IS NOT NULL OR ConfirmationStatus = 3)
        THROW 51212, N'Se aprobă doar ordinele de plată validate, nestornate și nerespinse.', 1;
    IF @Approve = 1 AND @RequireApproval = 1 AND EXISTS (SELECT 1 FROM @Locked WHERE ValidatedBy = @UserId)
        THROW 51213, N'Un ordin de plată validat de dumneavoastră se aprobă de alt utilizator (patru ochi).', 1;
    IF @Approve = 0 AND EXISTS (SELECT 1 FROM @Locked WHERE ConfirmationStatus = 2)
        THROW 51217, N'Ordinul de plată este deja confirmat pe extras.', 1;

    UPDATE o
    SET ApprovedAt = CASE WHEN @Approve = 1 THEN SYSUTCDATETIME() END, ApprovedBy = CASE WHEN @Approve = 1 THEN @UserId END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.PaymentOrder AS o JOIN @Locked AS k ON k.Id = o.Id;

    SELECT o.Id, o.RowVersion FROM fin.PaymentOrder AS o JOIN @Locked AS k ON k.Id = o.Id;
END;
