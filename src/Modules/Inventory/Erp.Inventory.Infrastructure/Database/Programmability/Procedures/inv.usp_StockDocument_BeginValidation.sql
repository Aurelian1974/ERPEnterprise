CREATE OR ALTER PROCEDURE inv.usp_StockDocument_BeginValidation
    @CompanyId  int,
    @Id         int,
    @RowVersion binary(8)
AS
BEGIN
    -- Inceputul validarii (02.05, S2 4.1–4.5): documentul blocat, ciorna, cu versiunea vazuta (54117, 54118), aprobat daca descarcarea o cere
    -- (54112), cu procesul-verbal atasat daca motivul il cere (54111). Numarul si miscarile le aloca apelantul, apoi inv.usp_StockDocument_MarkValidated.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Approval tinyint, @Version binary(8), @NeedsAttachment bit;
    SELECT @Status = d.Status, @Approval = d.ApprovalStatus, @Version = d.RowVersion, @NeedsAttachment = ISNULL(r.RequiresAttachment, 0)
    FROM inv.StockDocument AS d WITH (UPDLOCK, ROWLOCK)
    LEFT JOIN inv.MovementReason AS r ON r.Id = d.ReasonId
    WHERE d.Id = @Id AND d.CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 54001, N'Documentul nu există în firma curentă.', 1;
    IF @Version <> @RowVersion
        THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 1
        THROW 54118, N'Documentul nu mai este ciornă.', 1;
    IF @Approval IN (1, 3)
        THROW 54112, N'Descărcarea depășește pragul motivului și trebuie aprobată înainte de validare.', 1;
    IF @NeedsAttachment = 1 AND NOT EXISTS (SELECT 1 FROM core.Attachment WHERE EntityType = 'Inv.StockDocument' AND EntityId = @Id)
        THROW 54111, N'Motivul cere procesul-verbal atașat înainte de validare.', 1;
END;
